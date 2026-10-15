import AVFoundation
import UIKit

final class Decoder {
    let layer = AVSampleBufferDisplayLayer()
    private var format: CMVideoFormatDescription?
    private var paramSets: [[UInt8]] = []
    private var needKey = true

    init() {
        layer.videoGravity = .resizeAspect
        layer.backgroundColor = UIColor.black.cgColor
    }

    func waitForKeyframe() { needKey = true }

    // data = one H.265 frame in Annex B format
    func decode(_ data: [UInt8]) {
        var sets: [Int: [UInt8]] = [:]
        var avcc: [UInt8] = []
        var isKey = false

        for nal in split(data) {
            let type = Int(nal[nal.startIndex] >> 1) & 0x3F
            switch type {
            case 32...34:
                sets[type] = Array(nal)
            case 0...21:
                if type >= 19 { isKey = true }
                let n = nal.count
                avcc += [UInt8((n >> 24) & 0xFF), UInt8((n >> 16) & 0xFF),
                         UInt8((n >> 8) & 0xFF), UInt8(n & 0xFF)]
                avcc += nal
            default:
                break
            }
        }

        if let vps = sets[32], let sps = sets[33], let pps = sets[34] {
            let new = [vps, sps, pps]
            if new != paramSets || format == nil, let f = makeFormat(new) {
                format = f
                paramSets = new
            }
        }

        if needKey && !isKey { return }
        guard let format, !avcc.isEmpty else { return }
        needKey = false

        let n = avcc.count
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(
            allocator: nil, memoryBlock: nil, blockLength: n, blockAllocator: nil,
            customBlockSource: nil, offsetToData: 0, dataLength: n, flags: 0,
            blockBufferOut: &block) == noErr, let block else { return }
        let copied = avcc.withUnsafeBytes {
            CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block,
                                          offsetIntoDestination: 0, dataLength: n)
        }
        guard copied == noErr else { return }

        var timing = CMSampleTimingInfo(duration: .invalid,
                                        presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()),
                                        decodeTimeStamp: .invalid)
        var size = n
        var sample: CMSampleBuffer?
        guard CMSampleBufferCreateReady(
            allocator: nil, dataBuffer: block, formatDescription: format,
            sampleCount: 1, sampleTimingEntryCount: 1, sampleTimingArray: &timing,
            sampleSizeEntryCount: 1, sampleSizeArray: &size,
            sampleBufferOut: &sample) == noErr, let sample else { return }

        // show immediately
        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true),
           CFArrayGetCount(attachments) > 0 {
            let dict = unsafeBitCast(CFArrayGetValueAtIndex(attachments, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(dict,
                                 Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
                                 Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }

        if layer.status == .failed { layer.flush() }
        layer.enqueue(sample)
    }

    private func makeFormat(_ sets: [[UInt8]]) -> CMVideoFormatDescription? {
        var out: CMVideoFormatDescription?
        let status = sets[0].withUnsafeBufferPointer { a in
            sets[1].withUnsafeBufferPointer { b in
                sets[2].withUnsafeBufferPointer { c -> OSStatus in
                    let pointers = [a.baseAddress!, b.baseAddress!, c.baseAddress!]
                    let sizes = [a.count, b.count, c.count]
                    return CMVideoFormatDescriptionCreateFromHEVCParameterSets(
                        allocator: nil, parameterSetCount: 3,
                        parameterSetPointers: pointers, parameterSetSizes: sizes,
                        nalUnitHeaderLength: 4, extensions: nil, formatDescriptionOut: &out)
                }
            }
        }
        return status == noErr ? out : nil
    }

    // Annex B -> NAL list
    private func split(_ d: [UInt8]) -> [ArraySlice<UInt8>] {
        var result: [ArraySlice<UInt8>] = []
        var start = -1
        var i = 0
        while i + 2 < d.count {
            if d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1 {
                if start >= 0 {
                    var end = i
                    if end > start && d[end - 1] == 0 { end -= 1 }
                    if end > start { result.append(d[start..<end]) }
                }
                start = i + 3
                i += 3
            } else {
                i += 1
            }
        }
        if start >= 0 && start < d.count { result.append(d[start...]) }
        return result
    }
}
