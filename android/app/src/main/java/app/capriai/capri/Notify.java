package app.capriai.capri;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.os.Build;
import android.util.Log;
import org.json.JSONObject;

final class Notify {
    static final String ACTION_OPEN = "app.capriai.capri.NOTIFY_OPEN";
    static final String ACTION_ANSWER = "app.capriai.capri.NOTIFY_ANSWER";
    static final String KIND = "capri_kind";
    static final String JOB = "capri_job";
    static final String ASK = "capri_ask";
    static final String ID = "capri_id";
    static final String YES = "capri_yes";
    static final String NID = "capri_nid";
    // MainActivity is exported for the launcher and the /remote link, so any app can build an Intent with the action and
    // extras below. Only PendingIntents we made carry this per-process nonce; anything else is a forgery and is dropped.
    static final String TOKEN = "capri_token";
    private static final String NONCE = nonce();

    private static final String TAG = "CapriNotify";
    private static final String MESSAGES = "messages";
    private static final String ASKS = "asks";
    private static final String WISH = "notify";

    private Notify() {
    }

    private static String nonce() {
        byte[] bytes = new byte[16];
        new java.security.SecureRandom().nextBytes(bytes);
        StringBuilder text = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) text.append(String.format("%02x", b));
        return text.toString();
    }

    static boolean isOurs(Intent intent) {
        return intent != null && NONCE.equals(intent.getStringExtra(TOKEN));
    }

    // One stable notification id per message key. Hashing the key let two different messages land on the same id and
    // overwrite each other, so ids are handed out in order and remembered per key instead.
    private static int nidFor(Context context, String key) {
        SharedPreferences prefs = context.getSharedPreferences("capri", Context.MODE_PRIVATE);
        String remembered = prefs.getString("nid:" + key, null);
        if (remembered != null) {
            try {
                return Integer.parseInt(remembered);
            } catch (NumberFormatException ignored) {
                // fall through and give this key a fresh id
            }
        }
        int next = prefs.getInt("nid-next", 1000);
        if (next < 1000 || next > 200000) next = 1000;
        prefs.edit().putString("nid:" + key, String.valueOf(next)).putInt("nid-next", next + 1).apply();
        return next;
    }

    static boolean wished(Context context) {
        return context.getSharedPreferences("capri", Context.MODE_PRIVATE).getBoolean(WISH, false);
    }

    static void wish(Context context, boolean on) {
        SharedPreferences.Editor edit = context.getSharedPreferences("capri", Context.MODE_PRIVATE).edit();
        edit.putBoolean(WISH, on).apply();
    }

    static boolean granted(Context context) {
        if (Build.VERSION.SDK_INT >= 33
                && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            return false;
        }
        return context.getSystemService(NotificationManager.class).areNotificationsEnabled();
    }

    static boolean listening(Context context) {
        return wished(context) && granted(context);
    }

    static String info(Context context) {
        try {
            JSONObject out = new JSONObject();
            out.put("on", wished(context));
            out.put("allowed", granted(context));
            return out.toString();
        } catch (Exception e) {
            return "{\"on\":false,\"allowed\":false}";
        }
    }

    static String clean(String text, int max) {
        if (text == null) {
            return "";
        }
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (c == 0x200E || c == 0x200F || (c >= 0x202A && c <= 0x202E) || (c >= 0x2066 && c <= 0x2069)) {
                continue;
            }
            if (Character.isISOControl(c) || c == 0x2028 || c == 0x2029) {
                if (out.length() > 0 && out.charAt(out.length() - 1) != ' ') {
                    out.append(' ');
                }
                continue;
            }
            out.append(c);
        }
        String line = out.toString().trim();
        if (line.length() > max) {
            int cut = max;
            if (Character.isHighSurrogate(line.charAt(cut - 1))) {
                cut--;
            }
            line = line.substring(0, cut).trim();
        }
        return line;
    }

    static boolean show(Context context, String json) {
        if (json == null || json.length() > 8000 || !listening(context)) {
            return false;
        }
        try {
            JSONObject note = new JSONObject(json);
            String kind = clean(note.optString("kind"), 12);
            if (!kind.equals("reply") && !kind.equals("ask") && !kind.equals("done")
                    && !kind.equals("reminder") && !kind.equals("test") && !kind.equals("checkin")) {
                return false;
            }
            String id = clean(note.optString("id"), 100);
            String job = clean(note.optString("job"), 100);
            String askId = clean(note.optString("ask_id"), 100);
            String title = clean(note.optString("title"), 60);
            String body = clean(note.optString("body"), 200);
            long at = note.optLong("at", 0);
            boolean asking = kind.equals("ask");
            boolean jobbed = (asking || kind.equals("done")) && !job.isEmpty();
            String key = jobbed ? "job-" + job : "msg-" + (id.isEmpty() ? kind + at : id);
            int nid = nidFor(context, key);

            NotificationManager manager = context.getSystemService(NotificationManager.class);
            channels(manager);
            Notification.Builder builder = new Notification.Builder(context, asking ? ASKS : MESSAGES)
                    .setSmallIcon(R.drawable.ic_bubble)
                    .setContentTitle(title.isEmpty() ? "Capri" : title)
                    .setAutoCancel(true)
                    .setContentIntent(PendingIntent.getActivity(context, nid * 4,
                            base(context, Notify.ACTION_OPEN, kind, job, id, nid),
                            PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT));
            if (!body.isEmpty()) {
                builder.setContentText(body).setStyle(new Notification.BigTextStyle().bigText(body));
            }
            if (at > 0) {
                builder.setWhen(at).setShowWhen(true);
            }
            if (asking && !job.isEmpty()) {
                builder.addAction(answer(context, "Allow", true, nid, nid * 4 + 1, job, askId, id));
                builder.addAction(answer(context, "Deny", false, nid, nid * 4 + 2, job, askId, id));
            }
            manager.notify(nid, builder.build());
            return true;
        } catch (Exception e) {
            Log.w(TAG, "Notification not shown: " + e.getClass().getSimpleName());
            return false;
        }
    }

    private static void channels(NotificationManager manager) {
        if (manager.getNotificationChannel(MESSAGES) == null) {
            NotificationChannel channel = new NotificationChannel(MESSAGES, "Messages from Capri",
                    NotificationManager.IMPORTANCE_DEFAULT);
            channel.setDescription("Her answers, finished jobs and reminders while you are in another app.");
            manager.createNotificationChannel(channel);
        }
        if (manager.getNotificationChannel(ASKS) == null) {
            NotificationChannel channel = new NotificationChannel(ASKS, "Capri asking to go ahead",
                    NotificationManager.IMPORTANCE_HIGH);
            channel.setDescription("When a job on your PC waits for your say-so.");
            manager.createNotificationChannel(channel);
        }
    }

    private static Intent base(Context context, String action, String kind, String job, String id, int nid) {
        return new Intent(context, MainActivity.class)
                .setAction(action)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)
                .putExtra(TOKEN, NONCE)
                .putExtra(KIND, kind)
                .putExtra(JOB, job)
                .putExtra(ID, id)
                .putExtra(NID, nid);
    }

    private static Notification.Action answer(Context context, String label, boolean yes, int nid, int code,
            String job, String askId, String id) {
        Intent intent = base(context, Notify.ACTION_ANSWER, "ask", job, id, nid)
                .putExtra(ASK, askId)
                .putExtra(YES, yes);
        PendingIntent pending = PendingIntent.getActivity(context, code, intent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Action.Builder action = new Notification.Action.Builder(null, label, pending);
        if (Build.VERSION.SDK_INT >= 31) {
            action.setAuthenticationRequired(true);
        }
        return action.build();
    }
}
