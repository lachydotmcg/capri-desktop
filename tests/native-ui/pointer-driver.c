#define _POSIX_C_SOURCE 200809L
/* Disposable native QA input fixture. One persistent device preserves real
 * motion/button ordering. No recording; releases buttons on EOF/signal/timeout. */
#include <wayland-client.h>
#include <linux/input-event-codes.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <signal.h>
#include <unistd.h>
#include <poll.h>
#include <time.h>
#include <math.h>
#include "virtual-pointer.h"
static struct zwlr_virtual_pointer_manager_v1 *manager;
static volatile sig_atomic_t stop;
static void end(int s){(void)s;stop=1;}
static void global(void*d,struct wl_registry*r,uint32_t id,const char*n,uint32_t v){(void)d;(void)v;if(!strcmp(n,"zwlr_virtual_pointer_manager_v1"))manager=wl_registry_bind(r,id,&zwlr_virtual_pointer_manager_v1_interface,1);}
static void gone(void*d,struct wl_registry*r,uint32_t id){(void)d;(void)r;(void)id;}
static uint32_t now(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return (uint32_t)((uint64_t)t.tv_sec*1000+t.tv_nsec/1000000);}
int main(void){
 signal(SIGTERM,end);signal(SIGINT,end);signal(SIGALRM,end);alarm(300);
 struct wl_display*d=wl_display_connect(NULL);if(!d)return 2;
 struct wl_registry*r=wl_display_get_registry(d);struct wl_registry_listener l={global,gone};wl_registry_add_listener(r,&l,NULL);wl_display_roundtrip(d);if(!manager)return 3;
 struct zwlr_virtual_pointer_v1*p=zwlr_virtual_pointer_manager_v1_create_virtual_pointer(manager,NULL);wl_display_roundtrip(d);puts("READY");fflush(stdout);
 char line[128];int used=0;
 while(!stop){struct pollfd f={0,POLLIN,0};if(poll(&f,1,100)<=0)continue;char c;if(read(0,&c,1)!=1)break;if(c!='\n'){if(used>=126)break;line[used++]=c;continue;}line[used]=0;used=0;
  double x,y;int b,s;unsigned ax,ay,aw,ah;char extra;
  if(sscanf(line,"move %lf %lf %c",&x,&y,&extra)==2&&isfinite(x)&&isfinite(y)&&fabs(x)<=20000&&fabs(y)<=20000)zwlr_virtual_pointer_v1_motion(p,now(),wl_fixed_from_double(x),wl_fixed_from_double(y));
  else if(sscanf(line,"absolute %u %u %u %u %c",&ax,&ay,&aw,&ah,&extra)==4&&aw>0&&ah>0&&aw<=20000&&ah<=20000&&ax<aw&&ay<ah)zwlr_virtual_pointer_v1_motion_absolute(p,now(),ax,ay,aw,ah);
  else if(sscanf(line,"button %d %d %c",&b,&s,&extra)==2&&(b==BTN_LEFT||b==BTN_RIGHT||b==BTN_MIDDLE)&&(s==0||s==1))zwlr_virtual_pointer_v1_button(p,now(),b,s);
  else if(!strcmp(line,"quit"))break;
  else break;
  zwlr_virtual_pointer_v1_frame(p);if(wl_display_roundtrip(d)<0)break;puts("OK");fflush(stdout);
 }
 for(int b=BTN_LEFT;b<=BTN_MIDDLE;b++)zwlr_virtual_pointer_v1_button(p,now(),b,0);
 zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);zwlr_virtual_pointer_v1_destroy(p);wl_display_flush(d);wl_display_disconnect(d);return 0;
}
