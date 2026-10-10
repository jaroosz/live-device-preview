import Foundation

final class StepCounterSource: InputSource {
    var isAvailable: Bool { false } // TODO: Implement
    var bitMask: UInt16 { 1 << 10 }
    
    func updateMask(_ mask: UInt16) {
    }
    
    func appendData(to frame: inout Data, mask: UInt16) {
    }
}
