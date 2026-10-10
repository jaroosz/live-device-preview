import Network

struct Host: Identifiable {
    let name: String
    let ip: String
    let endpoint: NWEndpoint
    var id: String { name }
}

final class Browser {
    var onChange: (([Host]) -> Void)?
    private var browser: NWBrowser?
    private var discoveredHosts: [String: Host] = [:]

    func start() {
        let b = NWBrowser(for: .bonjour(type: "_mobiview._tcp", domain: nil), using: .tcp)
        b.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self = self else { return }
            var currentHosts: [String: Host] = [:]
            
            for r in results {
                if case let .service(name, _, _, _) = r.endpoint {
                    var displayName = self.discoveredHosts[name]?.name ?? name
                    var ipAddress = self.discoveredHosts[name]?.ip ?? ""
                    
                    if case let .bonjour(record) = r.metadata {
                        if let txtName = record.dictionary["name"], !txtName.isEmpty {
                            displayName = txtName
                        }
                        if let txtIp = record.dictionary["ip"], !txtIp.isEmpty {
                            ipAddress = txtIp
                        }
                    }
                    
                    let host = Host(name: displayName, ip: ipAddress, endpoint: r.endpoint)
                    currentHosts[name] = host
                    self.discoveredHosts[name] = host
                }
            }
            
            // Clean up missing hosts
            self.discoveredHosts = self.discoveredHosts.filter { currentHosts.keys.contains($0.key) }
            
            self.onChange?(self.discoveredHosts.values.sorted { $0.name < $1.name })
        }
        b.stateUpdateHandler = { [weak self] state in
            if case .failed = state {
                self?.browser?.cancel()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1) { self?.start() }
            }
        }
        browser = b
        b.start(queue: .main)
    }
}
