import Network
import UIKit

import CoreMotion

final class Session {
    let decoder = Decoder()
    let inputSender = InputSender()
    var onClosed: (() -> Void)?

    private let queue = DispatchQueue(label: "mobiview.session")
    private let tcp: NWConnection
    private var udp: NWConnection?
    private var host: NWEndpoint.Host?
    private var closed = false
    private var lastPacket = Date.distantPast
    private var pending: [Int: (total: Int, parts: [Int: [UInt8]])] = [:]
    private var lastSeq = 0
    private var hasLast = false

    private let headerSize = 31
    private let udpPort: NWEndpoint.Port = 7778

    init(endpoint: NWEndpoint) {
        tcp = NWConnection(to: endpoint, using: .tcp)
    }

    // MARK: - TCP (handshake + heartbeat)

    func start() {
        tcp.stateUpdateHandler = { [weak self] state in
            switch state {
            case .ready: self?.handshake()
            case .failed, .waiting, .cancelled: self?.close()
            default: break
            }
        }
        tcp.start(queue: queue)
    }

    func close() {
        queue.async {
            guard !self.closed else { return }
            self.closed = true
            self.tcp.cancel()
            self.udp?.cancel()
            DispatchQueue.main.async { self.onClosed?() }
        }
    }

    private func handshake() {
        if case let .hostPort(h, _)? = tcp.currentPath?.remoteEndpoint { host = h }
        let screen = UIScreen.main.nativeBounds
        let deviceId = UIDevice.current.identifierForVendor?.uuidString
            .replacingOccurrences(of: "-", with: "") ?? UUID().uuidString
            
        let motion = CMMotionManager()
        var supportedSensors: UInt16 = 1 // Touch
        if motion.isAccelerometerAvailable { supportedSensors |= (1 << 1) }
        if motion.isGyroAvailable { supportedSensors |= (1 << 2) }
        if motion.isDeviceMotionAvailable {
            supportedSensors |= (1 << 3) // Gravity
            supportedSensors |= (1 << 4) // Attitude
            supportedSensors |= (1 << 5) // LinearAccel
        }
        
        inputSender.supportedSensors = supportedSensors
        
        let hello: [String: Any] = [
            "type": "hello",
            "protocolVersion": 1,
            "deviceId": deviceId,
            "deviceName": UIDevice.current.name,
            "model": Session.model,
            "width": Int(screen.width),
            "height": Int(screen.height),
            "codecs": ["h265"],
            "supportedSensors": Int(supportedSensors)
        ]
        send(hello)
        readMessage { [weak self] data in
            guard let self else { return }
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  json["accepted"] as? Bool == true else { self.close(); return }
            self.startUDP()
            self.tick()
            self.heartbeat()
        }
    }

    private func heartbeat() {
        queue.asyncAfter(deadline: .now() + 5) { [weak self] in
            guard let self, !self.closed else { return }
            self.send(["type": "heartbeat", "protocolVersion": 1])
            self.readMessage { data in
                if data == nil { self.close() } else { self.heartbeat() }
            }
        }
    }

    private func send(_ object: [String: Any]) {
        guard let body = try? JSONSerialization.data(withJSONObject: object) else { return }
        let n = body.count
        var frame = Data()
        frame.append(UInt8((n >> 24) & 0xFF))
        frame.append(UInt8((n >> 16) & 0xFF))
        frame.append(UInt8((n >> 8) & 0xFF))
        frame.append(UInt8(n & 0xFF))
        frame.append(body)
        tcp.send(content: frame, completion: .contentProcessed { [weak self] error in
            if error != nil { self?.close() }
        })
    }

    private func read(_ n: Int, _ done: @escaping (Data?) -> Void) {
        tcp.receive(minimumIncompleteLength: n, maximumLength: n) { data, _, _, error in
            done(error == nil && data?.count == n ? data : nil)
        }
    }

    private func readMessage(_ done: @escaping (Data?) -> Void) {
        read(4) { [weak self] header in
            guard let self, let header else { return done(nil) }
            let length = header.reduce(0) { ($0 << 8) | Int($1) }
            guard length > 0, length <= 16 * 1024 else { return done(nil) }
            self.read(length, done)
        }
    }

    // MARK: - UDP (video)

    private func startUDP() {
        guard !closed, let host else { close(); return }
        let c = NWConnection(host: host, port: udpPort, using: .udp)
        udp = c
        inputSender.udpConnection = c
        
        c.stateUpdateHandler = { [weak self] state in
            switch state {
            case .ready:
                self?.receive(c)
                DispatchQueue.main.async { self?.inputSender.start() }
            case .failed, .waiting:
                self?.reopenUDP(c)
                DispatchQueue.main.async { self?.inputSender.stop() }
            default: break
            }
        }
        c.start(queue: queue)
    }

    private func reopenUDP(_ c: NWConnection) {
        guard udp === c else { return }
        udp = nil
        c.cancel()
        queue.asyncAfter(deadline: .now() + 1) { [weak self] in
            if let self, !self.closed { self.startUDP() }
        }
    }

    private func receive(_ c: NWConnection) {
        c.receiveMessage { [weak self] data, _, _, error in
            guard let self, self.udp === c, !self.closed else { return }
            if let data, !data.isEmpty { self.handle([UInt8](data)) }
            if error != nil { self.reopenUDP(c); return }
            self.receive(c)
        }
    }

    private func tick() {
        queue.asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self, !self.closed else { return }
            if let c = self.udp, Date().timeIntervalSince(self.lastPacket) > 1.5 {
                c.send(content: Data([0]), completion: .contentProcessed { _ in })
            }
            self.tick()
        }
    }

    private func le(_ p: [UInt8], _ offset: Int, _ count: Int) -> Int {
        (0..<count).reduce(0) { $0 | (Int(p[offset + $1]) << (8 * $1)) }
    }

    private func handle(_ p: [UInt8]) {
        lastPacket = Date()
        guard p.count >= headerSize, p[0] == 1 else { return }
        
        let mask = UInt16(le(p, 29, 2))
        DispatchQueue.main.async {
            self.inputSender.updateMask(mask)
        }

        let seq = le(p, 1, 4)
        let index = le(p, 21, 2)
        let total = le(p, 23, 2)
        guard total > 0, index < total else { return }

        if hasLast && seq < lastSeq {
            pending.removeAll()
            hasLast = false
            decoder.waitForKeyframe()
        }

        var entry = pending[seq] ?? (total: total, parts: [:])
        entry.parts[index] = Array(p[headerSize...])
        pending[seq] = entry
        guard entry.parts.count == total else { return }

        var frame: [UInt8] = []
        for i in 0..<total { frame += entry.parts[i] ?? [] }
        pending = pending.filter { $0.key > seq }

        if hasLast && seq != lastSeq + 1 { decoder.waitForKeyframe() }
        lastSeq = seq
        hasLast = true
        decoder.decode(frame)
    }

    private static let model: String = {
        var info = utsname()
        uname(&info)
        return withUnsafePointer(to: &info.machine) {
            $0.withMemoryRebound(to: CChar.self, capacity: 1) { String(cString: $0) }
        }
    }()
}
