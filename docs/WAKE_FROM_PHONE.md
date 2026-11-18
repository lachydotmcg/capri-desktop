# Wake your PC from your phone

Capri Remote can turn your PC on from your phone with Wake-on-LAN.

**It only works when your phone is on the same Wi-Fi or network as your PC.** The wake signal can't cross the internet, and the relay can't send it for you because your PC is off.

## What you need

- Your PC plugged in with a **network cable**. Wi-Fi almost never wakes a PC that's off.
- Capri Remote paired with your PC, and opened at least once while your PC was on. That's when your phone learns which network card to wake.
- To send the signal:
  - **Android:** the Capri app. Tap **Wake PC** on the "PC offline" banner.
  - **iPhone or a browser:** browsers can't send the wake signal. Tap **Wake PC** to see your PC's MAC and broadcast address, and paste them into any free Wake-on-LAN app.

## Set up your PC once

### 1. Turn on Wake-on-LAN in your BIOS

Restart your PC and open the BIOS or UEFI setup (usually **Del**, **F2** or **F10** while it starts). Look for a setting with a name like:

- Wake on LAN, Power On By PCI-E, Resume by PCI-E Device, PME Event Wake Up, or Remote Wake Up

Turn it on. If you see **ErP** or **Deep Sleep**, turn that off, because it cuts power to the network card. Save and exit.

### 2. Let Windows wake from your network card

Open her Mainframe, click **Remote**, then **Wake from phone**. Capri checks your network card. If it isn't turned on yet, Windows asks for permission, then she turns on:

- "Allow this device to wake the computer" for your wired network card
- "Wake on Magic Packet" for that card

She then says whether it's ready.

To do it by hand: open **Device Manager**, expand **Network adapters**, open your Ethernet card, and:

- On **Power Management**, tick "Allow this device to wake the computer" and "Only allow a magic packet to wake the computer".
- On **Advanced**, set "Wake on Magic Packet" to Enabled.

### 3. If it wakes from sleep but not from shut down

Turn off Fast Startup: **Control Panel > Power Options > Choose what the power buttons do > Change settings that are currently unavailable**, then untick **Turn on fast startup**.

Some PCs only wake from sleep, never from fully shut down. Sleep is the most reliable.

## Mac and Linux

- **Mac:** System Settings > Battery or Energy > turn on **Wake for network access**.
- **Linux:** run `sudo ethtool -s <card> wol g` (for example `enp3s0`), and turn it on in the BIOS as above.

## It doesn't wake

- Is your phone on the same Wi-Fi as your PC? Mobile data won't work.
- Is your PC plugged in with a cable, and still plugged in at the wall?
- Open Capri Remote once while your PC is on, so your phone has its newest details.
- Some routers keep Wi-Fi devices apart from everything else, often called AP isolation or client isolation. Turn it off in your router's settings.
