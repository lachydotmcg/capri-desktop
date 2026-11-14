import { launch } from './cdp.mjs';
import { FakePc } from './fakes.mjs';
import { earlyAccess, startSite } from './site.mjs';

export async function roomRig({ features = ['chat', 'call'], native = false, port = 29881, chrome = 29891 } = {}) {
  const site = await startSite(port);
  let page;
  try {
    page = await launch({ port: chrome, args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() + `
      localStorage.setItem('capri-remote-pairing', JSON.stringify({device:true,pc:'Test PC',allowed:true}));
      window.cameraRequests = []; window.audioStarts = 0; window.audioStops = 0; window.contexts = [];
      const gum = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
      navigator.mediaDevices.getUserMedia = async constraints => {
        const request = {constraints}; cameraRequests.push(request); request.stream = await gum(constraints); return request.stream;
      };
      const Context = window.AudioContext;
      window.AudioContext = class extends Context {
        constructor() {
          super(); contexts.push(this);
          const resume = this.resume.bind(this);
          this.resume = () => window.blockSound ? Promise.resolve() : resume();
          const make = this.createBufferSource.bind(this);
          this.createBufferSource = () => {
            const source = make(), start = source.start.bind(source), stop = source.stop.bind(source);
            source.start = (...args) => { audioStarts++; return start(...args); };
            source.stop = (...args) => { audioStops++; return stop(...args); };
            return source;
          };
        }
      };
      window.xrState = {ends:0,cancels:0,hit:[0.2,-0.3,-2]};
      const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
      const projection = [1,0,0,0,0,1,0,0,0,0,-1,-1,0,0,-0.2,0];
      Object.defineProperty(navigator,'xr',{value:{
        isSessionSupported: async () => true,
        requestSession: async () => {
          const active = new EventTarget(); let ended = false;
          active.visibilityState = 'visible'; active.updateRenderState = () => {};
          active.requestReferenceSpace = async () => ({});
          active.requestHitTestSource = async () => {
            if (xrState.hold) await new Promise(resolve => {xrState.release = resolve;});
            return {cancel:() => {xrState.cancels++;}};
          };
          active.requestAnimationFrame = callback => setTimeout(() => {
            if (!ended) callback(performance.now(), {
              getHitTestResults: () => [{getPose:()=>({transform:{position:{x:xrState.hit[0],y:xrState.hit[1],z:xrState.hit[2]}}})}],
              getViewerPose: () => ({views:[{transform:{inverse:{matrix:identity}},projectionMatrix:projection}]})
            });
          },16);
          active.end = async () => {if (!ended) {ended=true;xrState.ends++;active.dispatchEvent(new Event('end'));}};
          xrState.active = active; return active;
        }
      }});
      window.XRWebGLLayer = class { constructor() {this.framebuffer=null;} };
      const context = HTMLCanvasElement.prototype.getContext;
      HTMLCanvasElement.prototype.getContext = function(type,...args) {
        return type === 'webgl' ? {bindFramebuffer(){},clearColor(){},clear(){},FRAMEBUFFER:1,COLOR_BUFFER_BIT:2,DEPTH_BUFFER_BIT:4} : context.call(this,type,...args);
      };
    ` });
    await page.send('Fetch.enable', { patterns: [{ urlPattern: '*relay.js' }] });
    page.on((method, params) => {
      if (method !== 'Fetch.requestPaused') return;
      const source = `window.pc = {sent:[]};
        window.CapriRelay = {clear(){},link(){return {
          open(pairing,hooks){pc.hooks=hooks;hooks.state('connected',{pc:'Test PC',demo:false,native:${native},features:${JSON.stringify(features)}});},
          send(message){pc.sent.push(message);if(message.type==='call.start') queueMicrotask(()=>pc.hooks.message({type:'call.state',on:true,voice:true,ears:true}));return true;},
          close(){},retry(){}
        };}};`;
      page.send('Fetch.fulfillRequest', { requestId: params.requestId, responseCode: 200, responseHeaders: [{ name: 'content-type', value: 'text/javascript' }], body: Buffer.from(source).toString('base64') }).catch(() => {});
    });
    await page.go('http://127.0.0.1:' + port + '/');
    await page.until("document.getElementById('app').dataset.screen === 'chat' && CapriRemote.state === 'connected'", 'fake PC chat');
    const pc = new FakePc({});
    pc.devices.set('room', {});
    pc.send = async (device, message) => page.run('pc.hooks.message(' + JSON.stringify(message) + ')');
    const click = id => page.send('Runtime.evaluate', { expression: 'document.getElementById(' + JSON.stringify(id) + ').click()', userGesture: true, awaitPromise: true });
    return {
      page, pc, click,
      open: async () => { await click('room-camera'); await click('room-open'); await page.until("!document.getElementById('room-controls').hidden", 'room camera'); },
      state: (next, info = {}) => page.run('pc.hooks.state(' + JSON.stringify(next) + ',' + JSON.stringify(info) + ')'),
      close: async () => { await page.close(); await site.close(); }
    };
  } catch (error) { if (page) await page.close(); await site.close(); throw error; }
}
