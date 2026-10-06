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
                VideoView(layer: session.decoder.layer).ignoresSafeArea()
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
    let layer: AVSampleBufferDisplayLayer

    func makeUIView(context: Context) -> UIView {
        let view = LayerHostView()
        view.backgroundColor = .black
        view.layer.addSublayer(layer)
        return view
    }

    func updateUIView(_ uiView: UIView, context: Context) {}
}

private final class LayerHostView: UIView {
    override func layoutSubviews() {
        super.layoutSubviews()
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.sublayers?.forEach { $0.frame = bounds }
        CATransaction.commit()
    }
}
