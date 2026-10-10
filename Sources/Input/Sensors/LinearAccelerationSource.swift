import Foundation

final class LinearAccelerationSource: InputSource {
    var isAvailable: Bool { false } // TODO: Implement
    var bitMask: UInt16 { 1 << 5 }
    
    func updateMask(_ mask: UInt16) {
    }
    
    func appendData(to frame: inout Data, mask: UInt16) {
    }
}
