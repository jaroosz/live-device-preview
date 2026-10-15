import Foundation
import CoreMotion

final class GyroscopeSource: InputSource {
    private let motionManager: CMMotionManager
    
    init(motionManager: CMMotionManager) {
        self.motionManager = motionManager
    }

    var isAvailable: Bool { motionManager.isGyroAvailable }
    var bitMask: UInt16 { 1 << 2 }
    
    func updateMask(_ mask: UInt16) {
        let needsUpdates = (mask & bitMask) != 0
        if needsUpdates && !motionManager.isGyroActive {
            motionManager.startGyroUpdates()
        } else if !needsUpdates && motionManager.isGyroActive {
            motionManager.stopGyroUpdates()
        }
    }
    
    func appendData(to frame: inout Data, mask: UInt16) {
        guard (mask & bitMask) != 0 else { return }
        
        var x: Float32 = 0
        var y: Float32 = 0
        var z: Float32 = 0
        
        if let data = motionManager.gyroData {
            x = Float32(data.rotationRate.x)
            y = Float32(data.rotationRate.y)
            z = Float32(data.rotationRate.z)
        }
        
        var xBits = x.bitPattern.littleEndian
        var yBits = y.bitPattern.littleEndian
        var zBits = z.bitPattern.littleEndian
        
        withUnsafeBytes(of: &xBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &yBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &zBits) { frame.append(contentsOf: $0) }
    }
}
