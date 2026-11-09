import AVFoundation
import Foundation

setvbuf(stdout, nil, _IONBF, 0)
signal(SIGPIPE, SIG_DFL)

func fail(_ code: Int32, _ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}

let asked = DispatchSemaphore(value: 0)
var allowed = false
switch AVCaptureDevice.authorizationStatus(for: .audio) {
case .authorized: allowed = true
case .notDetermined:
    AVCaptureDevice.requestAccess(for: .audio) { yes in allowed = yes; asked.signal() }
    asked.wait()
default: allowed = false
}
if !allowed { fail(3, "no mic permission") }

let engine = AVAudioEngine()
let input = engine.inputNode
let from = input.outputFormat(forBus: 0)
if from.sampleRate == 0 || from.channelCount == 0 { fail(4, "no mic") }
guard let to = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: true),
      let converter = AVAudioConverter(from: from, to: to) else { fail(5, "no converter") }

input.installTap(onBus: 0, bufferSize: 1600, format: from) { buffer, _ in
    let room = AVAudioFrameCount(Double(buffer.frameLength) * 16000 / from.sampleRate) + 32
    guard let out = AVAudioPCMBuffer(pcmFormat: to, frameCapacity: room) else { return }
    var given = false
    var problem: NSError?
    converter.convert(to: out, error: &problem) { _, status in
        if given { status.pointee = .noDataNow; return nil }
        given = true
        status.pointee = .haveData
        return buffer
    }
    if problem != nil || out.frameLength == 0 { return }
    let bytes = Int(out.frameLength) * 2
    if fwrite(out.int16ChannelData![0], 1, bytes, stdout) != bytes { exit(0) }
}

do { try engine.start() } catch { fail(6, "mic wouldn't start") }
signal(SIGTERM) { _ in exit(0) }
dispatchMain()
