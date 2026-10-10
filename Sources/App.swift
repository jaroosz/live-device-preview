import SwiftUI
import AVFoundation

@main
struct MobiViewApp: App {
    var body: some Scene {
        WindowGroup { ContentView() }
    }
}

final class Model: ObservableObject {
    @Published var hosts: [Host] = []
    @Published var session: Session?
    private let browser = Browser()

    init() {
        browser.onChange = { [weak self] hosts in self?.hosts = hosts }
        browser.start()
    }

    func connect(_ host: Host) {
        let s = Session(endpoint: host.endpoint)
        s.onClosed = { [weak self] in self?.session = nil }
        session = s
        s.start()
    }
}

struct ContentView: View {
    @StateObject private var model = Model()

    var body: some View {
        Group {
            if let session = model.session {
                VideoView(session: session).ignoresSafeArea()
            } else {
                List(model.hosts) { host in
                    Button(host.name) { model.connect(host) }
                }
                .overlay { if model.hosts.isEmpty { ProgressView() } }
            }
        }
        .background(Color.black.ignoresSafeArea())
        .statusBarHidden(model.session != nil)
        .onAppear { UIApplication.shared.isIdleTimerDisabled = true }
    }
}

struct VideoView: UIViewRepresentable {
    let session: Session

    func makeUIView(context: Context) -> UIView {
        let view = LayerHostView()
        view.session = session
        view.isMultipleTouchEnabled = true
        view.backgroundColor = .black
        view.layer.addSublayer(session.decoder.layer)
        return view
    }

    func updateUIView(_ uiView: UIView, context: Context) {}
}

private final class LayerHostView: UIView {
    weak var session: Session?

    override func layoutSubviews() {
        super.layoutSubviews()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.sublayers?.forEach { $0.frame = bounds }
        CATransaction.commit()
    }
    
    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        session?.inputSender.handleTouches(touches, phase: 0, in: self)
    }
    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        session?.inputSender.handleTouches(touches, phase: 1, in: self)
    }
    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        session?.inputSender.handleTouches(touches, phase: 2, in: self)
    }
    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        session?.inputSender.handleTouches(touches, phase: 3, in: self)
    }
}
