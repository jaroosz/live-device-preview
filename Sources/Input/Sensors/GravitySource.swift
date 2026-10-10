import Foundation
import CoreMotion

final class GravitySource: InputSource {
    private let motionManager: CMMotionManager
    
    init(motionManager: CMMotionManager) {
        self.motionManager = motionManager
    }

    var isAvailable: Bool { motionManager.isDeviceMotionAvailable }
    var bitMask: UInt16 { 1 << 3 }
    
    func updateMask(_ mask: UInt16) {
        let needsDeviceMotion = (mask & 56) != 0 // Gravity (8) | Attitude (16) | LinearAccel (32)
        if needsDeviceMotion && !motionManager.isDeviceMotionActive {
            motionManager.startDeviceMotionUpdates()
        } else if !needsDeviceMotion && motionManager.isDeviceMotionActive {
            motionManager.stopDeviceMotionUpdates()
        }
    }
    
    func appendData(to frame: inout Data, mask: UInt16) {
        guard (mask & bitMask) != 0 else { return }
        
        var x: Float32 = 0
        var y: Float32 = 0
        var z: Float32 = 0
        
        if let data = motionManager.deviceMotion {
            x = Float32(data.gravity.x)
            y = Float32(data.gravity.y)
            z = Float32(data.gravity.z)
        }
        
        var xBits = x.bitPattern.littleEndian
        var yBits = y.bitPattern.littleEndian
        var zBits = z.bitPattern.littleEndian
        
        withUnsafeBytes(of: &xBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &yBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &zBits) { frame.append(contentsOf: $0) }
    }
}
