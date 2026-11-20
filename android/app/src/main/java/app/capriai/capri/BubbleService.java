package app.capriai.capri;

import android.animation.ValueAnimator;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.view.animation.DecelerateInterpolator;
import android.widget.TextView;

public final class BubbleService extends Service {
    private static final String TAG = "CapriBubble";
    private static final String CHANNEL = "bubble";
    private static final String HIDE = "app.capriai.capri.HIDE";
    private static final int NOTE = 1;
    private static final long NAP_AFTER = 3 * 60 * 1000;
    private static final long BLINK_EVERY = 5200;
    private static final long WORDS_FOR = 7000;

    private static BubbleService live;

    private final Handler main = new Handler(Looper.getMainLooper());
    private WindowManager windows;
    private Sprite her;
    private WindowManager.LayoutParams herSpot;
    private TextView words;
    private WindowManager.LayoutParams wordsSpot;
    private TextView bin;
    private WindowManager.LayoutParams binSpot;
    private ValueAnimator settle;
    private long lastTouch;
    private final Runnable blink = this::blink;
    private final Runnable nap = this::nap;
    private final Runnable quiet = this::quiet;

    static void show(Context context) {
        if (!Settings.canDrawOverlays(context)) {
            return;
        }
        try {
            context.startForegroundService(new Intent(context, BubbleService.class));
        } catch (RuntimeException e) {
            Log.w(TAG, "Could not float Capri", e);
        }
    }

    static void hide(Context context) {
        if (live != null) {
            context.stopService(new Intent(context, BubbleService.class));
        }
    }

    static boolean floating() {
        return live != null;
    }

    static void say(String text) {
        if (live != null) {
            live.speak(text);
        }
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        live = this;
        windows = getSystemService(WindowManager.class);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        startAsForeground();
        if (intent != null && HIDE.equals(intent.getAction())) {
            getSharedPreferences("capri", MODE_PRIVATE).edit().putBoolean("bubble", false).apply();
            stopSelf();
            return START_NOT_STICKY;
        }
        if (her == null) {
            if (!Settings.canDrawOverlays(this)) {
                stopSelf();
                return START_NOT_STICKY;
            }
            appear();
        }
        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy() {
        main.removeCallbacksAndMessages(null);
        if (settle != null) {
            settle.cancel();
        }
        remove(her);
        remove(words);
        remove(bin);
        her = null;
        words = null;
        bin = null;
        if (live == this) {
            live = null;
        }
        super.onDestroy();
    }

    private void remove(View view) {
        if (view != null && view.isAttachedToWindow()) {
            try {
                windows.removeView(view);
            } catch (RuntimeException e) {
                Log.w(TAG, "Could not remove a bubble window", e);
            }
        }
    }

    private void startAsForeground() {
        NotificationManager notes = getSystemService(NotificationManager.class);
        if (notes.getNotificationChannel(CHANNEL) == null) {
            NotificationChannel channel = new NotificationChannel(CHANNEL, "Capri on your screen",
                    NotificationManager.IMPORTANCE_LOW);
            channel.setDescription("Shows while Capri floats over your other apps.");
            channel.setShowBadge(false);
            notes.createNotificationChannel(channel);
        }
        PendingIntent open = PendingIntent.getActivity(this, 0, openIntent(),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop = PendingIntent.getService(this, 1,
                new Intent(this, BubbleService.class).setAction(HIDE),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification note = new Notification.Builder(this, CHANNEL)
                .setSmallIcon(R.drawable.ic_bubble)
                .setContentTitle("Capri is on your screen")
                .setContentText("Tap her to chat. Drag her onto the X to put her away.")
                .setContentIntent(open)
                .setOngoing(true)
                .addAction(new Notification.Action.Builder(null, "Put her away", stop).build())
                .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTE, note, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTE, note);
        }
    }

    private Intent openIntent() {
        return new Intent(this, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
    }

    private int dp(float units) {
        return Math.round(units * getResources().getDisplayMetrics().density);
    }

    private WindowManager.LayoutParams spot(int width, int height, boolean touchable) {
        int flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN;
        if (!touchable) {
            flags |= WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE;
        }
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(width, height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, flags, PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.START;
        return params;
    }

    private DisplayMetrics screen() {
        DisplayMetrics metrics = new DisplayMetrics();
        windows.getDefaultDisplay().getRealMetrics(metrics);
        return metrics;
    }

    private void appear() {
        SharedPreferences prefs = getSharedPreferences("capri", MODE_PRIVATE);
        DisplayMetrics metrics = screen();
        int width = dp(72);
        int height = Math.round(width * Sprite.ASPECT);
        her = new Sprite(this);
        her.setContentDescription("Capri. Tap to chat with her.");
        herSpot = spot(width, height, true);
        herSpot.x = prefs.getInt("bubble-x", metrics.widthPixels - width);
        herSpot.y = prefs.getInt("bubble-y", metrics.heightPixels / 3);
        clampSpot(metrics);
        her.setOnTouchListener(new Drag());
        windows.addView(her, herSpot);

        bin = new TextView(this);
        bin.setText("✕");
        bin.setTextSize(26);
        bin.setTextColor(Color.WHITE);
        bin.setGravity(Gravity.CENTER);
        GradientDrawable ring = new GradientDrawable();
        ring.setShape(GradientDrawable.OVAL);
        ring.setColor(0xCC121519);
        ring.setStroke(dp(2), 0xFFFF5470);
        bin.setBackground(ring);
        bin.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        binSpot = spot(dp(64), dp(64), false);

        words = new TextView(this);
        words.setTextColor(Color.WHITE);
        words.setTextSize(14);
        words.setMaxWidth(dp(220));
        words.setMaxLines(5);
        words.setEllipsize(android.text.TextUtils.TruncateAt.END);
        words.setPadding(dp(12), dp(8), dp(12), dp(8));
        GradientDrawable box = new GradientDrawable();
        box.setColor(0xF0121519);
        box.setCornerRadius(dp(14));
        box.setStroke(dp(1), 0xFF9EF2DF);
        words.setBackground(box);
        words.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        wordsSpot = spot(WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT, false);

        touched();
        her.play(Sprite.WAKE, null);
    }

    private void clampSpot(DisplayMetrics metrics) {
        herSpot.x = Math.max(0, Math.min(herSpot.x, metrics.widthPixels - herSpot.width));
        herSpot.y = Math.max(0, Math.min(herSpot.y, metrics.heightPixels - herSpot.height));
    }

    private void touched() {
        lastTouch = System.currentTimeMillis();
        main.removeCallbacks(nap);
        main.postDelayed(nap, NAP_AFTER);
        main.removeCallbacks(blink);
        main.postDelayed(blink, BLINK_EVERY);
    }

    private void blink() {
        if (her == null) {
            return;
        }
        if (her.clip() == Sprite.IDLE) {
            her.play(Math.random() < 0.2 ? Sprite.GRIN : Sprite.BLINK, null);
        }
        main.postDelayed(blink, BLINK_EVERY + (long) (Math.random() * 4000));
    }

    private void nap() {
        if (her != null && her.clip() != Sprite.DANGLE) {
            main.removeCallbacks(blink);
            her.play(Sprite.SLEEP, null);
        }
    }

    private void wake(Runnable then) {
        if (her.clip() == Sprite.SLEEP) {
            her.play(Sprite.WAKE, then);
        } else if (then != null) {
            then.run();
        }
        touched();
    }

    private void speak(String text) {
        if (her == null) {
            return;
        }
        String line = text.length() > 280 ? text.substring(0, 279) + "…" : text;
        wake(() -> her.play(Sprite.GRIN, null));
        words.setText(line);
        placeWords();
        main.removeCallbacks(quiet);
        main.postDelayed(quiet, WORDS_FOR + Math.min(8000, line.length() * 40L));
    }

    private void quiet() {
        remove(words);
    }

    private void placeWords() {
        if (words == null) {
            return;
        }
        DisplayMetrics metrics = screen();
        words.measure(View.MeasureSpec.makeMeasureSpec(dp(220), View.MeasureSpec.AT_MOST),
                View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED));
        int width = words.getMeasuredWidth();
        boolean left = herSpot.x + herSpot.width / 2 < metrics.widthPixels / 2;
        wordsSpot.x = left ? herSpot.x + herSpot.width + dp(4) : herSpot.x - width - dp(4);
        wordsSpot.x = Math.max(0, Math.min(wordsSpot.x, metrics.widthPixels - width));
        wordsSpot.y = Math.max(0, herSpot.y + dp(8));
        if (words.isAttachedToWindow()) {
            windows.updateViewLayout(words, wordsSpot);
        } else if (words.getText().length() > 0) {
            windows.addView(words, wordsSpot);
        }
    }

    private void showBin(boolean on) {
        if (on && !bin.isAttachedToWindow()) {
            DisplayMetrics metrics = screen();
            binSpot.x = (metrics.widthPixels - binSpot.width) / 2;
            binSpot.y = metrics.heightPixels - binSpot.height - dp(72);
            windows.addView(bin, binSpot);
        } else if (!on) {
            remove(bin);
        }
    }

    private boolean overBin() {
        if (bin == null || !bin.isAttachedToWindow()) {
            return false;
        }
        int herX = herSpot.x + herSpot.width / 2;
        int herY = herSpot.y + herSpot.height / 2;
        int binX = binSpot.x + binSpot.width / 2;
        int binY = binSpot.y + binSpot.height / 2;
        int reach = dp(80);
        return (herX - binX) * (herX - binX) + (herY - binY) * (herY - binY) < reach * reach;
    }

    private void openChat() {
        try {
            startActivity(openIntent());
        } catch (RuntimeException e) {
            Log.w(TAG, "Could not open Capri", e);
        }
    }

    private void snap() {
        DisplayMetrics metrics = screen();
        clampSpot(metrics);
        int from = herSpot.x;
        int to = herSpot.x + herSpot.width / 2 < metrics.widthPixels / 2 ? 0 : metrics.widthPixels - herSpot.width;
        if (settle != null) {
            settle.cancel();
        }
        settle = ValueAnimator.ofInt(from, to);
        settle.setDuration(260);
        settle.setInterpolator(new DecelerateInterpolator());
        settle.addUpdateListener(animation -> {
            if (her == null || !her.isAttachedToWindow()) {
                return;
            }
            herSpot.x = (int) animation.getAnimatedValue();
            windows.updateViewLayout(her, herSpot);
            if (words != null && words.isAttachedToWindow()) {
                placeWords();
            }
        });
        settle.start();
        getSharedPreferences("capri", MODE_PRIVATE).edit()
                .putInt("bubble-x", to).putInt("bubble-y", herSpot.y).apply();
    }

    private final class Drag implements View.OnTouchListener {
        private final int slop = ViewConfiguration.get(BubbleService.this).getScaledTouchSlop();
        private float downX;
        private float downY;
        private int startX;
        private int startY;
        private boolean dragging;

        @Override
        public boolean onTouch(View view, MotionEvent event) {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    if (settle != null) {
                        settle.cancel();
                    }
                    downX = event.getRawX();
                    downY = event.getRawY();
                    startX = herSpot.x;
                    startY = herSpot.y;
                    dragging = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - downX;
                    float dy = event.getRawY() - downY;
                    if (!dragging && dx * dx + dy * dy > slop * slop) {
                        dragging = true;
                        her.play(Sprite.DANGLE, null);
                        main.removeCallbacks(blink);
                        main.removeCallbacks(nap);
                        showBin(true);
                    }
                    if (dragging) {
                        herSpot.x = startX + Math.round(dx);
                        herSpot.y = startY + Math.round(dy);
                        windows.updateViewLayout(her, herSpot);
                        if (words != null && words.isAttachedToWindow()) {
                            placeWords();
                        }
                        bin.setScaleX(overBin() ? 1.25f : 1f);
                        bin.setScaleY(overBin() ? 1.25f : 1f);
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    if (dragging) {
                        boolean away = overBin();
                        showBin(false);
                        if (away) {
                            stopSelf();
                            return true;
                        }
                        her.play(Sprite.IDLE, null);
                        snap();
                        touched();
                    } else if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                        view.performClick();
                        wake(BubbleService.this::openChat);
                    }
                    return true;
                default:
                    return false;
            }
        }
    }
}
