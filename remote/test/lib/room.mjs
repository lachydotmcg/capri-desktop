import { launch } from './cdp.mjs';
import { FakePc } from './fakes.mjs';
import { earlyAccess, startSite } from './site.mjs';

export async function roomRig({ features = ['chat', 'call'], native = false, port = 29881, chrome = 29891, heart = false } = {}) {
  const site = await startSite(port);
  let page, heartRequest, heartReady;
  const heartWaiting = new Promise(resolve => { heartReady = resolve; });
  const hearts = [];
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
      window.xrState = {ends:0,cancels:0,transientCancels:0,hit:[0.2,-0.3,-2],touch:null};
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
          if (!xrState.noTransient) active.requestHitTestSourceForTransientInput = async options => {
            xrState.profile = options.profile;
            if (xrState.rejectTransient) throw new Error('No transient hit tests');
            if (xrState.holdTransient) await new Promise(resolve => {xrState.releaseTransient = resolve;});
            return {cancel:() => {xrState.transientCancels++;}};
          };
          const finger = {};
          active.requestAnimationFrame = callback => setTimeout(() => {
            if (!ended) callback(performance.now(), {
              getHitTestResults: () => [{getPose:()=>({transform:{position:{x:xrState.hit[0],y:xrState.hit[1],z:xrState.hit[2]}}})}],
              getHitTestResultsForTransientInput: () => [{inputSource:finger,results:xrState.touch ? [{getPose:()=>({transform:{position:{x:xrState.touch[0],y:xrState.touch[1],z:xrState.touch[2]}}})}] : []}],
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
    await page.send('Fetch.enable', { patterns: [{ urlPattern: '*relay.js' }, ...(heart ? [{ urlPattern: '*sprites/heart.png' }] : [])] });
    page.on((method, params) => {
      if (method !== 'Fetch.requestPaused') return;
      if (params.request.url.endsWith('/sprites/heart.png')) {
        hearts.push(params.requestId);
        if (hearts.length > 1) { page.send('Fetch.continueRequest', {requestId:params.requestId}).catch(() => {}); return; }
        heartRequest = params.requestId; heartReady(); return;
      }
      const source = `window.pc = {sent:[]};
        window.CapriRelay = {clear(){},link(){return {
          open(pairing,hooks){pc.hooks=hooks;hooks.state('connected',{pc:'Test PC',demo:false,native:${native},features:${JSON.stringify(features)}});},
          send(message){pc.sent.push(message);if(message.type==='call.start' && !pc.noAnswer) queueMicrotask(()=>pc.hooks.message(pc.answer || {type:'call.state',on:true,voice:true,ears:true}));return true;},
          close(){},retry(){}
        };}};`;
      page.send('Fetch.fulfillRequest', { requestId: params.requestId, responseCode: 200, responseHeaders: [{ name: 'content-type', value: 'text/javascript' }], body: Buffer.from(source).toString('base64') }).catch(() => {});
    });
    await page.go('http://127.0.0.1:' + port + '/');
    await page.until("document.getElementById('app').dataset.screen === 'chat' && CapriRemote.state === 'connected' && window.CapriCall", 'fake PC chat');
    const pc = new FakePc({});
    pc.devices.set('room', {});
    pc.send = async (device, message) => page.run('pc.hooks.message(' + JSON.stringify(message) + ')');
    const click = id => page.send('Runtime.evaluate', { expression: 'document.getElementById(' + JSON.stringify(id) + ').click()', userGesture: true, awaitPromise: true });
    return {
      page, pc, click,
      heart: { requests: hearts, ready: heartWaiting, finish: async (fail = false) => {
        await heartWaiting;
        await page.send(fail ? 'Fetch.failRequest' : 'Fetch.continueRequest', fail ? {requestId:heartRequest,errorReason:'Failed'} : {requestId:heartRequest});
      } },
      open: async () => { await click('room-camera'); await click('room-open'); await page.until("!document.getElementById('room-controls').hidden", 'room camera'); },
      state: (next, info = {}) => page.run('pc.hooks.state(' + JSON.stringify(next) + ',' + JSON.stringify(info) + ')'),
      close: async () => { await page.close(); await site.close(); }
    };
  } catch (error) { if (page) await page.close(); await site.close(); throw error; }
}

export const roomPosition = page => page.run(`(() => {
  const her = document.getElementById('room-her'), view = document.getElementById('room-view');
  const box = document.getElementById('room-sprite').getBoundingClientRect();
  return {left:her.offsetLeft,top:her.offsetTop,width:view.clientWidth,height:view.clientHeight,
    x:box.left + box.width / 2,y:box.top + box.height / 2};
})()`);

export async function roomPointer(page, type, action, x, y) {
  if (type === 'touch') {
    await page.send('Input.dispatchTouchEvent', {type:action === 'down' ? 'touchStart' : action === 'up' ? 'touchEnd' : 'touchMove',
      touchPoints:action === 'up' ? [] : [{x,y,id:1}]});
  } else {
    await page.send('Input.dispatchMouseEvent', {type:action === 'down' ? 'mousePressed' : action === 'up' ? 'mouseReleased' : 'mouseMoved',
      x,y,button:'left',buttons:action === 'up' ? 0 : 1,clickCount:action === 'move' ? 0 : 1,pointerType:type});
  }
  await page.run('new Promise(resolve => requestAnimationFrame(() => resolve()))');
  if (action === 'up') await page.run('new Promise(resolve => setTimeout(resolve,600))');
}

export async function dragRoom(page, type, dx, dy) {
  const start = await roomPosition(page);
  await roomPointer(page,type,'down',start.x,start.y);
  await roomPointer(page,type,'move',start.x + dx,start.y + dy);
  const live = await roomPosition(page);
  await roomPointer(page,type,'up',start.x + dx,start.y + dy);
  return {start,live,end:await roomPosition(page)};
}
