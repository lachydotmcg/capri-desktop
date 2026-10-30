FROM archlinux@sha256:e82623936fa1a69700b48e98b058df6eaab18b737c27473a7234a40fdecb9413

RUN pacman -Syu --noconfirm --needed base-devel dotnet-sdk python git \
    wayland libxkbcommon libxcb fontconfig freetype2 libx11 libice libsm libxrandr \
    libxext icu openssl glib2 libpulse libarchive xorg-server-xvfb xorg-xauth \
    openbox xcompmgr dbus ttf-dejavu ffmpeg libxtst && \
    useradd --create-home --uid 1000 builder && \
    pacman -Q > /toolchain-packages.txt

USER builder
WORKDIR /home/builder
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
