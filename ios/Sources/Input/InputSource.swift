import Foundation

protocol InputSource {
    var isAvailable: Bool { get }
    var bitMask: UInt16 { get }
    func updateMask(_ mask: UInt16)
    func appendData(to frame: inout Data, mask: UInt16)
}

