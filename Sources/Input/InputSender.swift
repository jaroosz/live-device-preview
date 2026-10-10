import Foundation
import QuartzCore
import Network
import UIKit

final class InputSender {
    let touchSource = TouchSource()
    private var sources: [InputSource] = []
    
    private var currentMask: UInt16 = 0
    var supportedSensors: UInt16 = 1
    private var frameId: UInt32 = 0
    
    private let historyLimit = 3
    private var frameHistory: [Data] = []
    
    private var displayLink: CADisplayLink?
    weak var udpConnection: NWConnection?
    
    init() {
        sources = [
            touchSource,
            AccelerometerSource(),
            GyroscopeSource(),
            GravitySource(),
            AttitudeSource(),
            LinearAccelerationSource(),
            MagneticFieldSource(),
            LightSource(),
            ProximitySource(),
            PressureSource(),
            StepCounterSource(),
            PenSource(),
            GamepadSource(),
            KeyboardSource()
        ]
    }
    
    func start() {
        displayLink?.invalidate()
        let link = CADisplayLink(target: self, selector: #selector(tick))
        link.preferredFramesPerSecond = 60
        link.add(to: .main, forMode: .common)
        displayLink = link
    }
    
    func stop() {
        displayLink?.invalidate()
        displayLink = nil
    }
    
    func updateMask(_ newMask: UInt16) {
        guard currentMask != newMask else { return }
        currentMask = newMask
        for source in sources {
            source.updateMask(newMask)
        }
    }
    
    @objc private func tick() {
        guard currentMask != 0, let udp = udpConnection else { return }
        
        frameId &+= 1
        
        var currentFrame = Data()
        var fId = frameId.littleEndian
        withUnsafeBytes(of: &fId) { currentFrame.append(contentsOf: $0) }
        
        let actualMask = currentMask & supportedSensors
        
        for source in sources {
            source.appendData(to: &currentFrame, mask: actualMask)
        }
        
        frameHistory.append(currentFrame)
        if frameHistory.count > historyLimit {
            frameHistory.removeFirst()
        }
        
        var packet = Data()
        packet.append(3)
        packet.append(UInt8(frameHistory.count))
        
        var mask = actualMask.littleEndian
        withUnsafeBytes(of: &mask) { packet.append(contentsOf: $0) }
        
        for historyFrame in frameHistory {
            packet.append(historyFrame)
        }
        
        udp.send(content: packet, completion: .contentProcessed { _ in })
    }

    func handleTouches(_ touches: Set<UITouch>, phase: UInt8, in view: UIView) {
        touchSource.processTouches(touches, phase: phase, in: view)
    }
}
