import SwiftUI
import AVFoundation

extension Color {
    static let unityBg = Color(red: 40/255, green: 40/255, blue: 40/255)
    static let unityPanel = Color(red: 56/255, green: 56/255, blue: 56/255)
    static let unityBorder = Color(red: 34/255, green: 34/255, blue: 34/255)
    static let unityText = Color(red: 204/255, green: 204/255, blue: 204/255)
    static let unityTitle = Color(red: 238/255, green: 238/255, blue: 238/255)
    static let unityMuted = Color(red: 153/255, green: 153/255, blue: 153/255)
}

@main
struct LiveDevicePreviewApp: App {
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

    func connect(_ host: Host, deviceName: String) {
        let s = Session(endpoint: host.endpoint, deviceName: deviceName)
        s.onClosed = { [weak self] in self?.session = nil }
        session = s
        s.start()
    }
}

struct ContentView: View {
    @StateObject private var model = Model()
    @State private var dotCount = 0
    @State private var isShowingOptions = false
    @AppStorage("deviceName") private var deviceName = UIDevice.current.name
    @AppStorage("disableHaptics") private var disableHaptics = false
    
    private var deviceModel: String {
        var systemInfo = utsname()
        uname(&systemInfo)
        let machineMirror = Mirror(reflecting: systemInfo.machine)
        let identifier = machineMirror.children.reduce("") { identifier, element in
            guard let value = element.value as? Int8, value != 0 else { return identifier }
            return identifier + String(UnicodeScalar(UInt8(value)))
        }
        return identifier
    }

    var body: some View {
        Group {
            if let session = model.session {
                VideoView(session: session).ignoresSafeArea()
            } else {
                VStack(spacing: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        ZStack {
                            Text("Live Device Preview")
                                .font(.system(size: 24, weight: .bold))
                                .foregroundColor(.unityTitle)
                            
                            HStack {
                                Spacer()
                                Button(action: { isShowingOptions = true }) {
                                    Image(systemName: "gearshape")
                                        .font(.system(size: 20))
                                        .foregroundColor(.unityText)
                                        .frame(width: 32, height: 32)
                                        .background(Color.clear)
                                        .cornerRadius(4)
                                }
                            }
                        }
                        
                        Divider()
                            .background(Color.unityBorder)
                            .padding(.vertical, 12)
                        
                        VStack(alignment: .leading, spacing: 6) {
                            HStack(spacing: 4) {
                                Text("Device model:")
                                    .font(.system(size: 11))
                                    .foregroundColor(.unityMuted)
                                    .frame(width: 85, alignment: .leading)
                                Text(deviceModel)
                                    .font(.system(size: 11))
                                    .foregroundColor(.unityText)
                            }
                            
                            HStack(spacing: 4) {
                                Text("Device name:")
                                    .font(.system(size: 11))
                                    .foregroundColor(.unityMuted)
                                    .frame(width: 85, alignment: .leading)
                                Text(deviceName)
                                    .font(.system(size: 11))
                                    .foregroundColor(.unityText)
                            }
                        }
                    }
                    .padding(.horizontal, 15)
                    .padding(.bottom, 12)
                    .padding(.top, 12)
                    .background(Color.unityPanel.ignoresSafeArea(.all, edges: .top))
                    .overlay(
                        Rectangle().frame(height: 1).foregroundColor(.unityBorder),
                        alignment: .bottom
                    )
                    
                    ZStack {
                        Color.unityBg.ignoresSafeArea()
                        
                        if model.hosts.isEmpty {
                            VStack {
                                HStack(spacing: 6) {
                                    Circle().fill(Color.unityText).frame(width: 5, height: 5)
                                        .offset(y: dotCount == 0 ? -5 : 0)
                                    Circle().fill(Color.unityText).frame(width: 5, height: 5)
                                        .offset(y: dotCount == 1 ? -5 : 0)
                                    Circle().fill(Color.unityText).frame(width: 5, height: 5)
                                        .offset(y: dotCount == 2 ? -5 : 0)
                                }
                                .animation(.easeInOut(duration: 0.3), value: dotCount)
                                .onAppear {
                                    Timer.scheduledTimer(withTimeInterval: 0.3, repeats: true) { _ in
                                        dotCount = (dotCount + 1) % 4
                                    }
                                }
                                
                                Text("Searching for devices...")
                                    .font(.system(size: 12))
                                    .foregroundColor(.unityMuted)
                                    .padding(.top, 15)
                            }
                        } else {
                            ScrollView {
                                VStack(alignment: .leading, spacing: 12) {
                                    Text("AVAILABLE DEVICES")
                                        .font(.system(size: 11, weight: .bold))
                                        .foregroundColor(.unityMuted)
                                        .padding(.bottom, 5)
                                    
                                    ForEach(model.hosts) { host in
                                        Button(action: { model.connect(host, deviceName: deviceName) }) {
                                            HStack(spacing: 16) {
                                                Image(systemName: "desktopcomputer")
                                                    .font(.system(size: 24))
                                                    .foregroundColor(.unityMuted)
                                                
                                                VStack(alignment: .leading, spacing: 4) {
                                                    Text(host.name)
                                                        .font(.system(size: 16, weight: .medium))
                                                        .foregroundColor(.unityTitle)
                                                    
                                                    if !host.ip.isEmpty {
                                                        Text(host.ip)
                                                            .font(.system(size: 13))
                                                            .foregroundColor(.unityMuted)
                                                    }
                                                }
                                                Spacer()
                                            }
                                            .padding(.vertical, 12)
                                            .padding(.horizontal, 16)
                                            .background(Color.unityPanel)
                                            .cornerRadius(6)
                                            .overlay(
                                                RoundedRectangle(cornerRadius: 6)
                                                    .stroke(Color.unityBorder, lineWidth: 1)
                                            )
                                        }
                                        .buttonStyle(PlainButtonStyle())
                                    }
                                }
                                .padding(15)
                            }
                        }
                    }
                }
            }
        }
        .statusBarHidden(model.session != nil)
        .onAppear {
            UIApplication.shared.isIdleTimerDisabled = true
            deviceName = String(deviceName.prefix(32))
        }
        .fullScreenCover(isPresented: $isShowingOptions) {
            OptionsView(deviceName: $deviceName, disableHaptics: $disableHaptics)
        }
    }
}

private struct OptionsView: View {
    @Environment(\.dismiss) private var dismiss
    @Binding var deviceName: String
    @Binding var disableHaptics: Bool

    private var limitedDeviceName: Binding<String> {
        Binding(
            get: { deviceName },
            set: { deviceName = String($0.prefix(32)) }
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            // Top Bar
            ZStack {
                Text("Options")
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundColor(.unityTitle)
                
                HStack {
                    Spacer()
                    Button("Done") { dismiss() }
                        .font(.system(size: 16, weight: .bold))
                        .foregroundColor(deviceName.count >= 3 ? Color(red: 0.3, green: 0.6, blue: 1.0) : .gray)
                        .disabled(deviceName.count < 3)
                }
            }
            .padding()
            .background(Color.unityPanel.ignoresSafeArea(.all, edges: .top))
            
            Divider().background(Color.unityBorder)
            
            // Content
            ZStack {
                Color.unityBg.ignoresSafeArea()
                
                VStack(spacing: 0) {
                    HStack {
                        Text("Device Name")
                            .font(.system(size: 15))
                            .foregroundColor(.unityTitle)
                        
                        Spacer()
                        
                        TextField("Name", text: limitedDeviceName)
                            .font(.system(size: 15))
                            .foregroundColor(.unityText)
                            .multilineTextAlignment(.trailing)
                            .colorScheme(.dark)
                            .frame(maxWidth: 160)
                    }
                    .padding(.vertical, 16)
                    
                    Divider().background(Color.unityBorder)
                    
                    HStack {
                        Text("Disable haptics")
                            .font(.system(size: 15))
                            .foregroundColor(.unityTitle)
                        
                        Spacer()
                        
                        Toggle("", isOn: $disableHaptics)
                            .labelsHidden()
                            .tint(Color(red: 0.3, green: 0.6, blue: 1.0))
                    }
                    .padding(.vertical, 10)
                    
                    Divider().background(Color.unityBorder)
                    
                    if deviceName.count < 3 {
                        Text("Device name must be at least 3 characters.")
                            .font(.system(size: 11))
                            .foregroundColor(.red)
                            .padding(.top, 12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    
                    Spacer()
                }
                .padding(.horizontal, 20)
            }
        }
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
