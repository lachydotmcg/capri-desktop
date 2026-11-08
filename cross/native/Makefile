CC ?= cc
CFLAGS ?= -O2

capri-input: capri-input.c virtual-pointer.c virtual-keyboard.c virtual-pointer.h virtual-keyboard.h
	$(CC) $(CFLAGS) -std=c11 -Wall -Wextra -Werror -o $@ capri-input.c virtual-pointer.c virtual-keyboard.c $$(pkg-config --cflags --libs wayland-client xkbcommon xcb) -lm

.PHONY: clean
clean:
	rm -f capri-input
