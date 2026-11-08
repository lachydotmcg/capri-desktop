#define _GNU_SOURCE
/* Bounded compositor-session injection. No capture, shell, clipboard or key logging.
 * Protocol declarations are reused from the previously qualified native fixtures.
 * The parent sends the payload first, verifies the live target after READY, then
 * sends G. SIGTERM/SIGINT are cleanup requests, never an unhandled held key.
 */
#include <wayland-client.h>
#include <xkbcommon/xkbcommon.h>
#include <xcb/xcb.h>
#include <errno.h>
#include <linux/input-event-codes.h>
#include <sys/mman.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <unistd.h>
#include <signal.h>
#include <poll.h>
#include <math.h>
#include <time.h>
#include "virtual-pointer.h"
#include "virtual-keyboard.h"
static struct zwlr_virtual_pointer_manager_v1 *pointer_manager;
static struct zwp_virtual_keyboard_manager_v1 *keyboard_manager;
static struct wl_seat *seat;
static volatile sig_atomic_t stopped;
static uint32_t timestamp(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return (uint32_t)((uint64_t)t.tv_sec*1000+t.tv_nsec/1000000);}
static void stop(int sig) { (void)sig; stopped=1; }
static void global(void *data,struct wl_registry *r,uint32_t id,const char *name,uint32_t v) {
 (void)data;(void)v;
 if(!strcmp(name,"zwlr_virtual_pointer_manager_v1"))pointer_manager=wl_registry_bind(r,id,&zwlr_virtual_pointer_manager_v1_interface,1);
 if(!strcmp(name,"zwp_virtual_keyboard_manager_v1"))keyboard_manager=wl_registry_bind(r,id,&zwp_virtual_keyboard_manager_v1_interface,1);
 if(!strcmp(name,"wl_seat")&&!seat)seat=wl_registry_bind(r,id,&wl_seat_interface,1);
}
static void gone(void *data,struct wl_registry *r,uint32_t id){(void)data;(void)r;(void)id;}
static int read_byte(void) {
 struct pollfd p={.fd=STDIN_FILENO,.events=POLLIN};
 for(int i=0;i<100&&!stopped;i++) {
  int n=poll(&p,1,20);if(n<0)continue;
  if(n>0){unsigned char b;return read(0,&b,1)==1?b:-1;}
 }
 return -1;
}
static uint32_t utf8(const unsigned char *s,size_t n,size_t *used) {
 uint32_t v;if(!n)return 0;
 if(s[0]<128){*used=1;return s[0];}
 int bytes;
 if((s[0]&0xe0)==0xc0)bytes=2;
 else if((s[0]&0xf0)==0xe0)bytes=3;
 else if((s[0]&0xf8)==0xf0)bytes=4;
 else return 0;
 if(n<(size_t)bytes)return 0;
 v=s[0]&((1u<<(7-bytes))-1);
 for(int i=1;i<bytes;i++){if((s[i]&0xc0)!=0x80)return 0;v=(v<<6)|(s[i]&0x3f);}
 if(v>0x10ffff||(v>=0xd800&&v<=0xdfff)||(bytes==2&&v<128)||(bytes==3&&v<2048)||(bytes==4&&v<65536))return 0;
 *used=(size_t)bytes;return v;
}
/* Read-only X11 keyboard ownership. A separate process keeps a stalled server
 * outside Capri's UI/runtime; the parent also imposes a cancellable deadline. */
static int x11_focus(int argc,char **argv) {
 if(argc!=3||!argv[2][0]||strspn(argv[2],"0123456789")!=strlen(argv[2]))return 2;
 errno=0;char *end;unsigned long target=strtoul(argv[2],&end,10);
 if(errno||*end||target<=1||target>UINT32_MAX)return 2;
 alarm(2);
 xcb_connection_t *connection=xcb_connect(NULL,NULL);
 if(!connection)return 3;
 int result=3,focused=0;
 if(xcb_connection_has_error(connection))goto done;
 xcb_generic_error_t *error=NULL;
 xcb_get_input_focus_reply_t *focus=xcb_get_input_focus_reply(connection,xcb_get_input_focus(connection),&error);
 if(error||!focus){free(error);free(focus);goto done;}
 xcb_window_t window=focus->focus;free(focus);
 for(int depth=0;depth<32&&window>1;depth++) {
  if(window==target){focused=1;break;}
  xcb_query_tree_reply_t *tree=xcb_query_tree_reply(connection,xcb_query_tree(connection,window),&error);
  if(error||!tree){free(error);free(tree);goto done;}
  xcb_window_t parent=tree->parent;free(tree);
  if(parent==window)break;
  window=parent;
 }
 puts(focused?"FOCUSED":"UNFOCUSED");result=0;
done:
 xcb_disconnect(connection);return result;
}
int main(int argc,char **argv) {
 if(argc<2)return 2;
 if(!strcmp(argv[1],"--x11-focus"))return x11_focus(argc,argv);
 signal(SIGTERM,stop);signal(SIGINT,stop);signal(SIGHUP,stop);
 int validation=!strcmp(argv[1],"--validate-text");
 struct wl_display *d=NULL;
 if(!validation){
  d=wl_display_connect(NULL);if(!d)return 3;
  struct wl_registry *r=wl_display_get_registry(d);const struct wl_registry_listener listener={global,gone};
  wl_registry_add_listener(r,&listener,NULL);if(wl_display_roundtrip(d)<0)return 4;
  if(!strcmp(argv[1],"--probe")){printf("{\"virtual_pointer\":%s,\"virtual_keyboard\":%s}\n",pointer_manager?"true":"false",keyboard_manager&&seat?"true":"false");wl_display_disconnect(d);return 0;}
 }
 int mouse=!strcmp(argv[1],"click")||!strcmp(argv[1],"double_click")||!strcmp(argv[1],"drag")||!strcmp(argv[1],"scroll");
 if(!validation&&((mouse&&!pointer_manager)||(!mouse&&(!keyboard_manager||!seat))))return 5;
 unsigned char payload[8193];size_t size=0;int ch;
 /* Decimal size line, then exactly that many UTF-8 bytes. No secrets in argv. */
 char count[8];size_t ci=0;
 while((ch=read_byte())!='\n'){if(ch<'0'||ch>'9'||ci>=6)return 6;count[ci++]=(char)ch;}
 count[ci]=0;size=(size_t)strtoul(count,NULL,10);if(size>8192)return 6;
 for(size_t i=0;i<size;i++){ch=read_byte();if(ch<0)return 6;payload[i]=(unsigned char)ch;}payload[size]=0;
 struct zwlr_virtual_pointer_v1 *p=NULL;struct zwp_virtual_keyboard_v1 *k=NULL;
 struct xkb_context *ctx=NULL;struct xkb_keymap *map=NULL;
 uint32_t codes[512],unique[240];size_t length=0,unique_count=0;
 int error=0;uint32_t held=UINT32_MAX;uint32_t modifiers=0;
 if(mouse)p=zwlr_virtual_pointer_manager_v1_create_virtual_pointer(pointer_manager,NULL);
 else {
  if(!validation)k=zwp_virtual_keyboard_manager_v1_create_virtual_keyboard(keyboard_manager,seat);
  ctx=xkb_context_new(XKB_CONTEXT_NO_FLAGS);
  if(!strcmp(argv[1],"text")||validation) {
   for(size_t offset=0;offset<size;){size_t used=0;uint32_t cp=utf8(payload+offset,size-offset,&used);if(!cp||length>=512){error=7;goto cleanup;}offset+=used;
    size_t i;for(i=0;i<unique_count&&unique[i]!=cp;i++);if(i==unique_count){if(unique_count>=240){error=7;goto cleanup;}unique[unique_count++]=cp;}codes[length++]=(uint32_t)i;
   }
   char keymap[48000];size_t at=0;
   at+=(size_t)snprintf(keymap+at,sizeof(keymap)-at,"xkb_keymap { xkb_keycodes \"capri\" { minimum=8; maximum=255;");
   for(size_t i=0;i<unique_count;i++)at+=(size_t)snprintf(keymap+at,sizeof(keymap)-at,"<K%03zu>=%zu;",i,i+8);
   at+=(size_t)snprintf(keymap+at,sizeof(keymap)-at,"}; xkb_types \"capri\" { type \"ONE_LEVEL\" { modifiers=None; level_name[Level1]=\"Any\"; }; }; xkb_compatibility \"capri\" {}; xkb_symbols \"capri\" {");
   for(size_t i=0;i<unique_count;i++)at+=(size_t)snprintf(keymap+at,sizeof(keymap)-at,"key <K%03zu> { type=\"ONE_LEVEL\", [ U%08X ] };",i,unique[i]);
   snprintf(keymap+at,sizeof(keymap)-at,"}; };");
   map=xkb_keymap_new_from_string(ctx,keymap,XKB_KEYMAP_FORMAT_TEXT_V1,XKB_KEYMAP_COMPILE_NO_FLAGS);
  } else if(!strcmp(argv[1],"key")) {
   map=xkb_keymap_new_from_names(ctx,NULL,XKB_KEYMAP_COMPILE_NO_FLAGS);
  } else {error=2;goto cleanup;}
  if(!map){error=7;goto cleanup;}
  if(validation){puts("KEYMAP_VALID");goto cleanup;}
  char *text=xkb_keymap_get_as_string(map,XKB_KEYMAP_FORMAT_TEXT_V1);int fd=memfd_create("capri-input-keymap",MFD_CLOEXEC);if(fd<0){free(text);error=8;goto cleanup;}
  size_t bytes=strlen(text)+1;if(write(fd,text,bytes)!=(ssize_t)bytes){close(fd);free(text);error=8;goto cleanup;}
  zwp_virtual_keyboard_v1_keymap(k,WL_KEYBOARD_KEYMAP_FORMAT_XKB_V1,fd,(uint32_t)bytes);close(fd);free(text);wl_display_roundtrip(d);
 }
 puts("READY");fflush(stdout);
 if(read_byte()!='G'){error=9;goto cleanup;}
 if(stopped){error=10;goto cleanup;}
 if(!strcmp(argv[1],"drag")) {
  double dx,dy;int duration;char extra;
  if(sscanf((char*)payload,"%lf %lf %d %c",&dx,&dy,&duration,&extra)!=3||!isfinite(dx)||!isfinite(dy)||fabs(dx)>20000||fabs(dy)>20000||duration<100||duration>1500){error=7;goto cleanup;}
  held=BTN_LEFT;zwlr_virtual_pointer_v1_button(p,timestamp(),held,WL_POINTER_BUTTON_STATE_PRESSED);zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);
  for(int step=1;step<=20&&!stopped;step++){
   printf("STEP %d\n",step);fflush(stdout);if(read_byte()!='G'||stopped){error=10;goto cleanup;}
   zwlr_virtual_pointer_v1_motion(p,timestamp(),wl_fixed_from_double(dx/20),wl_fixed_from_double(dy/20));zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);usleep((useconds_t)(duration*1000/20));
  }
  zwlr_virtual_pointer_v1_button(p,timestamp(),held,WL_POINTER_BUTTON_STATE_RELEASED);held=UINT32_MAX;zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);
 } else if(!strcmp(argv[1],"click")||!strcmp(argv[1],"double_click")) {
  if(!((size==4&&!memcmp(payload,"left",4))||(size==5&&!memcmp(payload,"right",5)))){error=7;goto cleanup;}
  for(int repeat=0;repeat<(!strcmp(argv[1],"double_click")?2:1)&&!stopped;repeat++) {
  uint32_t button=size==5&&!memcmp(payload,"right",5)?BTN_RIGHT:BTN_LEFT;
  held=button;zwlr_virtual_pointer_v1_button(p,timestamp(),button,WL_POINTER_BUTTON_STATE_PRESSED);zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);usleep(20000);
  zwlr_virtual_pointer_v1_button(p,timestamp(),button,WL_POINTER_BUTTON_STATE_RELEASED);held=UINT32_MAX;zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);
  }
 } else if(!strcmp(argv[1],"scroll")) {
  char *end;long amount=strtol((char*)payload,&end,10);if(*end||!amount||labs(amount)>1000){error=7;goto cleanup;}
  zwlr_virtual_pointer_v1_axis_source(p,WL_POINTER_AXIS_SOURCE_WHEEL);
  zwlr_virtual_pointer_v1_axis(p,timestamp(),WL_POINTER_AXIS_VERTICAL_SCROLL,wl_fixed_from_int((int)amount));zwlr_virtual_pointer_v1_frame(p);wl_display_roundtrip(d);
 } else if(!strcmp(argv[1],"text")) {
  for(size_t i=0;i<length&&!stopped;i++) {
   held=codes[i];zwp_virtual_keyboard_v1_key(k,timestamp(),held,WL_KEYBOARD_KEY_STATE_PRESSED);wl_display_roundtrip(d);usleep(3000);
   zwp_virtual_keyboard_v1_key(k,timestamp(),held,WL_KEYBOARD_KEY_STATE_RELEASED);held=UINT32_MAX;wl_display_roundtrip(d);
  }
 } else {
  static const char *names[]={"ENTER","ESC","TAB","BACKSPACE","DELETE","UP","DOWN","LEFT","RIGHT","HOME","END","PAGEUP","PAGEDOWN","CTRL+A","CTRL+L","CTRL+S","CTRL+Z","CTRL+Y","CTRL+W","CTRL+T","CTRL+TAB","CTRL+SHIFT+TAB","CTRL+C","CTRL+V","CTRL+R","ALT+F4","ALT+LEFT","ALT+RIGHT","SPACE","CTRL+SHIFT+M","CTRL+SHIFT+D"};
  static const uint32_t keys[]={KEY_ENTER,KEY_ESC,KEY_TAB,KEY_BACKSPACE,KEY_DELETE,KEY_UP,KEY_DOWN,KEY_LEFT,KEY_RIGHT,KEY_HOME,KEY_END,KEY_PAGEUP,KEY_PAGEDOWN,KEY_A,KEY_L,KEY_S,KEY_Z,KEY_Y,KEY_W,KEY_T,KEY_TAB,KEY_TAB,KEY_C,KEY_V,KEY_R,KEY_F4,KEY_LEFT,KEY_RIGHT,KEY_SPACE,KEY_M,KEY_D};
  int found=-1;for(size_t i=0;i<sizeof(keys)/sizeof(keys[0]);i++)if(strlen(names[i])==size&&!memcmp(payload,names[i],size))found=(int)i;
  if(found<0){error=7;goto cleanup;}
  static const char *mod_names[]={XKB_MOD_NAME_CTRL,XKB_MOD_NAME_ALT,XKB_MOD_NAME_SHIFT};
  const int bits[3]={strstr((char*)payload,"CTRL+")!=NULL,strstr((char*)payload,"ALT+")!=NULL,strstr((char*)payload,"SHIFT+")!=NULL};
  for(int m=0;m<3;m++)if(bits[m]){xkb_mod_index_t index=xkb_keymap_mod_get_index(map,mod_names[m]);if(index==XKB_MOD_INVALID||index>=32){error=7;goto cleanup;}modifiers|=1u<<index;}
  if(modifiers)zwp_virtual_keyboard_v1_modifiers(k,modifiers,0,0,0);
  held=keys[found];zwp_virtual_keyboard_v1_key(k,timestamp(),held,WL_KEYBOARD_KEY_STATE_PRESSED);wl_display_roundtrip(d);usleep(20000);
  zwp_virtual_keyboard_v1_key(k,timestamp(),held,WL_KEYBOARD_KEY_STATE_RELEASED);held=UINT32_MAX;zwp_virtual_keyboard_v1_modifiers(k,0,0,0,0);modifiers=0;wl_display_roundtrip(d);
 }
 if(stopped)error=10;
cleanup:
 if(p){if(held!=UINT32_MAX){zwlr_virtual_pointer_v1_button(p,timestamp(),held,WL_POINTER_BUTTON_STATE_RELEASED);zwlr_virtual_pointer_v1_frame(p);}zwlr_virtual_pointer_v1_destroy(p);}
 if(k){if(held!=UINT32_MAX)zwp_virtual_keyboard_v1_key(k,timestamp(),held,WL_KEYBOARD_KEY_STATE_RELEASED);if(modifiers)zwp_virtual_keyboard_v1_modifiers(k,0,0,0,0);zwp_virtual_keyboard_v1_destroy(k);}
 if(d){wl_display_roundtrip(d);wl_display_disconnect(d);}
 if(map)xkb_keymap_unref(map);
 if(ctx)xkb_context_unref(ctx);
 return error;
}
