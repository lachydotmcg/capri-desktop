import os, subprocess, pathlib, signal, time, sys
root=pathlib.Path(__file__).resolve().parents[2]
out=pathlib.Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=False,mode=0o700)
env=os.environ.copy()
for key in ['WAYLAND_DISPLAY','HYPRLAND_INSTANCE_SIGNATURE','DBUS_SESSION_BUS_ADDRESS']:env.pop(key,None)
for key,name in [('XDG_CONFIG_HOME','config'),('XDG_CACHE_HOME','cache'),('XDG_DATA_HOME','data'),('XDG_STATE_HOME','state'),('XDG_RUNTIME_DIR','runtime')]:
 p=out/name;p.mkdir(mode=0o700);env[key]=str(p)
env.update(XDG_SESSION_TYPE='x11',XDG_CURRENT_DESKTOP='Openbox',CAPRI_PRIVATE_UI_TEST='1')
private_display=next(n for n in range(191,220) if not pathlib.Path('/tmp/.X11-unix/X'+str(n)).exists() and not pathlib.Path('/tmp/.X'+str(n)+'-lock').exists())
children=[];recorder=None;xvfb=None
try:
 xvfb=subprocess.Popen(['Xvfb',':'+str(private_display),'-screen','0','1280x900x24','-nolisten','tcp'],stdout=subprocess.PIPE,stderr=(out/'xvfb.log').open('w'),env=env)
 env['DISPLAY']=':'+str(private_display)
 for attempt in range(100):
  if pathlib.Path('/tmp/.X11-unix/X'+str(private_display)).exists():break
  if xvfb.poll() is not None:raise RuntimeError('Private Xvfb failed')
  time.sleep(0.02)
 else:raise RuntimeError('Private Xvfb not ready')
 children.append(subprocess.Popen(['openbox'],env=env,stdout=(out/'wm.log').open('w'),stderr=subprocess.STDOUT))
 recorder=subprocess.Popen(['ffmpeg','-nostdin','-hide_banner','-loglevel','error','-f','x11grab','-framerate','30','-video_size','1280x900','-i',env['DISPLAY'],'-c:v','libx264','-preset','ultrafast','-crf','18',str(out/'ui-transitions.mkv')],env=env,stdout=(out/'recording.log').open('w'),stderr=subprocess.STDOUT)
 cmd=['dotnet',str(root/'tests/mainframe/bin/Release/net10.0/Capri.MainframeChecks.dll'),str(out/'captures')]
 result=subprocess.run(cmd,env=env,cwd=out,stdout=(out/'app.log').open('w'),stderr=subprocess.STDOUT,timeout=50)
 print('exit',result.returncode);raise SystemExit(result.returncode)
finally:
 if recorder and recorder.poll() is None:recorder.send_signal(signal.SIGINT);recorder.wait(timeout=8)
 for child in children:
  if child.poll() is None:child.terminate();child.wait(timeout=4)
 if xvfb is not None and xvfb.poll() is None:xvfb.terminate();xvfb.wait(timeout=4)
