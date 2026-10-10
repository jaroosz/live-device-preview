import UIKit

final class TouchSource: InputSource {
    var isAvailable: Bool { true }
    var bitMask: UInt16 { 1 }
    
    private var activeTouches: [UITouch: (id: UInt8, phase: UInt8, x: Float, y: Float)] = [:]
    private var touchIdCounter: UInt8 = 0
    
    func updateMask(_ mask: UInt16) {
        if (mask & 1) == 0 {
            activeTouches.removeAll()
        }
    }

    func appendData(to frame: inout Data, mask: UInt16) {
        guard (mask & 1) != 0 else { return }
        
        frame.append(UInt8(activeTouches.count))
        
        for state in activeTouches.values {
            frame.append(state.id)
            frame.append(state.phase)
            
            var x = state.x.bitPattern.littleEndian
            withUnsafeBytes(of: &x) { frame.append(contentsOf: $0) }
            
            var y = state.y.bitPattern.littleEndian
            withUnsafeBytes(of: &y) { frame.append(contentsOf: $0) }
        }
        
        activeTouches = activeTouches.filter { $0.value.phase < 2 }
    }
    
    func processTouches(_ touches: Set<UITouch>, phase: UInt8, in view: UIView) {
        let bounds = view.bounds
        guard bounds.width > 0, bounds.height > 0 else { return }
        
        for touch in touches {
            let loc = touch.location(in: view)
            let x = Float(loc.x / bounds.width)
            let y = Float(1.0 - (loc.y / bounds.height))
            
            if phase == 0 {
                let id = touchIdCounter
                touchIdCounter = touchIdCounter &+ 1
                activeTouches[touch] = (id: id, phase: phase, x: x, y: y)
            } else if var state = activeTouches[touch] {
                state.phase = phase
                state.x = x
                state.y = y
                activeTouches[touch] = state
            }
        }
    }
}
