import Foundation
import CoreMotion

final class AttitudeSource: InputSource {
    private let motionManager: CMMotionManager
    
    init(motionManager: CMMotionManager) {
        self.motionManager = motionManager
    }

    var isAvailable: Bool { motionManager.isDeviceMotionAvailable }
    var bitMask: UInt16 { 1 << 4 }
    
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
        var w: Float32 = 0
        
        if let data = motionManager.deviceMotion {
            x = Float32(data.attitude.quaternion.x)
            y = Float32(data.attitude.quaternion.y)
            z = Float32(data.attitude.quaternion.z)
            w = Float32(data.attitude.quaternion.w)
        }
        
        var xBits = x.bitPattern.littleEndian
        var yBits = y.bitPattern.littleEndian
        var zBits = z.bitPattern.littleEndian
        var wBits = w.bitPattern.littleEndian
        
        withUnsafeBytes(of: &xBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &yBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &zBits) { frame.append(contentsOf: $0) }
        withUnsafeBytes(of: &wBits) { frame.append(contentsOf: $0) }
    }
}
