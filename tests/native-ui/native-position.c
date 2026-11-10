#define _POSIX_C_SOURCE 200809L
#include <X11/Xlib.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include <poll.h>
#include <unistd.h>
static long long stamp(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return (long long)t.tv_sec*1000000000+t.tv_nsec;}
int main(int argc,char**argv){if(argc!=3)return 2;Display*d=XOpenDisplay(0);if(!d)return 3;Window w=strtoul(argv[1],0,0);int ms=atoi(argv[2]);if(ms<1||ms>60000)return 4;XSelectInput(d,w,StructureNotifyMask);XSync(d,0);puts("tick,x,y,width,height");fflush(stdout);long long end=stamp()+(long long)ms*1000000;while(stamp()<end){while(XPending(d)){XEvent e;XNextEvent(d,&e);if(e.type==ConfigureNotify&&e.xconfigure.window==w){printf("%lld,%d,%d,%d,%d\n",stamp(),e.xconfigure.x,e.xconfigure.y,e.xconfigure.width,e.xconfigure.height);fflush(stdout);}if(e.type==DestroyNotify)return 0;}struct pollfd p={ConnectionNumber(d),POLLIN,0};poll(&p,1,2);}XCloseDisplay(d);return 0;}
