package app.capriai.capri;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;

public final class ListenService extends Service {
    private static final String TAG = "CapriListen";
    private static final String CHANNEL = "listen";
    private static final String STOP = "app.capriai.capri.LISTEN_STOP";
    private static final int NOTE = 2;

    static boolean start(Context context) {
        try {
            context.startForegroundService(new Intent(context, ListenService.class));
            return true;
        } catch (RuntimeException e) {
            Log.w(TAG, "Could not keep listening", e);
            return false;
        }
    }

    static void stop(Context context) {
        context.stopService(new Intent(context, ListenService.class));
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        startAsForeground();
        if (intent != null && STOP.equals(intent.getAction())) {
            Notify.wish(this, false);
            stopSelf();
            MainActivity.listenStopped();
        }
        return START_NOT_STICKY;
    }

    private void startAsForeground() {
        NotificationManager notes = getSystemService(NotificationManager.class);
        if (notes.getNotificationChannel(CHANNEL) == null) {
            NotificationChannel channel = new NotificationChannel(CHANNEL, "Capri is listening",
                    NotificationManager.IMPORTANCE_LOW);
            channel.setDescription("Shows while Capri waits to tell you something from your PC.");
            channel.setShowBadge(false);
            notes.createNotificationChannel(channel);
        }
        Intent openIntent = new Intent(this, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
        PendingIntent open = PendingIntent.getActivity(this, 2, openIntent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop = PendingIntent.getService(this, 3,
                new Intent(this, ListenService.class).setAction(STOP),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification note = new Notification.Builder(this, CHANNEL)
                .setSmallIcon(R.drawable.ic_bubble)
                .setContentTitle("Capri will let you know when she needs you")
                .setContentIntent(open)
                .setOngoing(true)
                .addAction(new Notification.Action.Builder(null, "Stop", stop).build())
                .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTE, note, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTE, note);
        }
    }
}
