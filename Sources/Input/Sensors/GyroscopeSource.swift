import Foundation

final class GyroscopeSource: InputSource {
    var isAvailable: Bool { false } // TODO: Implement
    var bitMask: UInt16 { 1 << 2 }
    
    func updateMask(_ mask: UInt16) {
    }
    
    func appendData(to frame: inout Data, mask: UInt16) {
    }
}
