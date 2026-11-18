package app.capriai.capri;

import android.util.Log;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.LinkedHashSet;
import java.util.Set;
import java.util.regex.Pattern;
import org.json.JSONArray;
import org.json.JSONObject;

final class Wake {
    private static final String TAG = "Capri";
    private static final Pattern MAC = Pattern.compile("^[0-9A-F]{2}(:[0-9A-F]{2}){5}$");
    private static final Pattern IP = Pattern.compile("^(\\d{1,3}\\.){3}\\d{1,3}$");
    private static final int[] PORTS = {9, 7};

    private Wake() {
    }

    static byte[] packet(String mac) {
        if (mac == null || !MAC.matcher(mac).matches()) {
            return null;
        }
        byte[] address = new byte[6];
        String[] parts = mac.split(":");
        for (int i = 0; i < 6; i++) {
            address[i] = (byte) Integer.parseInt(parts[i], 16);
        }
        byte[] packet = new byte[102];
        for (int i = 0; i < 6; i++) {
            packet[i] = (byte) 0xFF;
        }
        for (int i = 0; i < 16; i++) {
            System.arraycopy(address, 0, packet, 6 + i * 6, 6);
        }
        return packet;
    }

    static int send(String cards) {
        int sent = 0;
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            JSONArray list = new JSONArray(cards);
            for (int i = 0; i < Math.min(4, list.length()); i++) {
                JSONObject card = list.optJSONObject(i);
                if (card == null) {
                    continue;
                }
                byte[] packet = packet(card.optString("mac"));
                if (packet == null) {
                    continue;
                }
                Set<String> targets = new LinkedHashSet<>();
                String broadcast = card.optString("broadcast");
                if (IP.matcher(broadcast).matches()) {
                    targets.add(broadcast);
                }
                targets.add("255.255.255.255");
                for (String target : targets) {
                    InetAddress to;
                    try {
                        to = InetAddress.getByName(target);
                    } catch (Exception e) {
                        continue;
                    }
                    for (int port : PORTS) {
                        for (int round = 0; round < 3; round++) {
                            try {
                                socket.send(new DatagramPacket(packet, packet.length, to, port));
                                sent++;
                            } catch (Exception e) {
                                Log.w(TAG, "Wake packet not sent: " + e.getClass().getSimpleName());
                            }
                        }
                    }
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "Wake failed: " + e.getClass().getSimpleName());
        }
        return sent;
    }
}
