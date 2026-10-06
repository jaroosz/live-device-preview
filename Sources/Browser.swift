import Network

struct Host: Identifiable {
    let name: String
    let endpoint: NWEndpoint
    var id: String { name }
}

final class Browser {
    var onChange: (([Host]) -> Void)?
    private var browser: NWBrowser?

    func start() {
        let b = NWBrowser(for: .bonjour(type: "_mobiview._tcp", domain: nil), using: .tcp)
        b.browseResultsChangedHandler = { [weak self] results, _ in
            var map: [String: Host] = [:]
            for r in results {
                if case let .service(name, _, _, _) = r.endpoint, map[name] == nil {
                    map[name] = Host(name: name, endpoint: r.endpoint)
                }
            }
            self?.onChange?(map.values.sorted { $0.name < $1.name })
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
