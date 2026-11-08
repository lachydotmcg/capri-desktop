/* Capri Remote's call: the microphone's samples for call.js, about 20 ms at a time. Loaded only while he's talking. */
class CapriMic extends AudioWorkletProcessor {
  constructor() {
    super();
    this.block = new Float32Array(1024);
    this.used = 0;
  }

  process(inputs) {
    const channel = inputs[0] && inputs[0][0];
    if (channel) {
      for (let i = 0; i < channel.length; i++) {
        this.block[this.used++] = channel[i];
        if (this.used === this.block.length) {
          this.port.postMessage(this.block);
          this.block = new Float32Array(1024);
          this.used = 0;
        }
      }
    }
    return true;
  }
}
registerProcessor('capri-mic', CapriMic);
