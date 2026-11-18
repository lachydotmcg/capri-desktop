package app.capriai.capri;

import android.Manifest;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.app.NotificationManager;
import android.os.Bundle;
import android.os.SystemClock;
import android.provider.Settings;
import android.util.Log;
import android.webkit.ConsoleMessage;
import android.webkit.CookieManager;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import com.google.ar.core.ArCoreApk;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import org.json.JSONObject;

public final class MainActivity extends Activity {
    static final String HOME = "https://" + WebFiles.HOST + "/remote/";
    private static final String TAG = "Capri";
    private static final int MEDIA_REQUEST = 7;
    private static final int FILE_REQUEST = 8;
    private static final int NOTIFY_REQUEST = 9;

    private WebView web;
    private WebFiles files;
    private PermissionRequest pendingMedia;
    private ValueCallback<Uri[]> pendingFile;
    private volatile boolean arSupported;
    private volatile boolean visible;
    private static volatile MainActivity current;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        current = this;
        files = new WebFiles(getAssets());
        if ((getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0) {
            WebView.setWebContentsDebuggingEnabled(true);
        }
        web = new WebView(this);
        web.setBackgroundColor(0xFF0A0A10);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setSupportMultipleWindows(false);
        settings.setUserAgentString(settings.getUserAgentString() + " CapriAndroid/" + BuildConfig.VERSION_NAME);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);
        web.addJavascriptInterface(new Bridge(), "CapriAndroid");
        web.setWebViewClient(new Client());
        web.setWebChromeClient(new Chrome());
        setContentView(web);
        checkAr();
        if (state != null) {
            web.restoreState(state);
        }
        if (web.getUrl() == null) {
            web.loadUrl(startUrl(getIntent()));
        }
        if (state == null) {
            handleNotify(getIntent());
        }
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        if (handleNotify(intent)) {
            return;
        }
        Uri link = intent.getData();
        if (link == null && intent.getStringExtra("url") == null) {
            return;
        }
        String next = startUrl(intent);
        String now = web.getUrl();
        if (now != null && strip(now).equals(strip(next))) {
            web.evaluateJavascript("location.replace(" + JSONObject.quote(next) + ");location.reload();", null);
        } else {
            web.loadUrl(next);
        }
    }

    @Override
    protected void onStart() {
        super.onStart();
        visible = true;
        web.onResume();
        BubbleService.hide(this);
        ListenService.stop(this);
    }

    @Override
    protected void onResume() {
        super.onResume();
        checkAr();
        web.evaluateJavascript("window.dispatchEvent(new Event('capri-android'))", null);
    }

    @Override
    protected void onStop() {
        visible = false;
        if (bubbleWanted() && Settings.canDrawOverlays(this)) {
            BubbleService.show(this);
        } else if (!Notify.listening(this) || !ListenService.start(this)) {
            web.onPause();
        }
        super.onStop();
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        web.saveState(state);
    }

    @Override
    protected void onDestroy() {
        BubbleService.hide(this);
        ListenService.stop(this);
        if (current == this) {
            current = null;
        }
        web.destroy();
        super.onDestroy();
    }

    @Override
    public void onBackPressed() {
        web.evaluateJavascript("window.CapriAndroidBack ? CapriAndroidBack() : false", value -> {
            if ("true".equals(value)) {
                return;
            }
            if (web.canGoBack()) {
                web.goBack();
            } else {
                moveTaskToBack(true);
            }
        });
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(code, permissions, results);
        if (code == MEDIA_REQUEST && pendingMedia != null) {
            grantMedia(pendingMedia);
            pendingMedia = null;
        }
        if (code == NOTIFY_REQUEST) {
            web.evaluateJavascript("window.dispatchEvent(new Event('capri-android'))", null);
        }
    }

    static void listenStopped() {
        MainActivity now = current;
        if (now != null) {
            now.runOnUiThread(() -> {
                if (!now.visible && now.web != null) {
                    now.web.onPause();
                }
            });
        }
    }

    private boolean handleNotify(Intent intent) {
        String action = intent == null ? null : intent.getAction();
        boolean open = Notify.ACTION_OPEN.equals(action);
        boolean answer = Notify.ACTION_ANSWER.equals(action);
        if (!open && !answer) {
            return false;
        }
        // The activity is exported for the launcher and the /remote link, so "open" and "answer" can be forged by any
        // app on the phone. Only our own PendingIntents carry the process nonce; anything else is ignored.
        if (!Notify.isOurs(intent)) {
            Log.w(TAG, "Dropped a notification intent that did not come from us");
            intent.setAction(null);
            return false;
        }
        intent.setAction(null);
        try {
            JSONObject payload = new JSONObject();
            if (answer) {
                payload.put("job", intent.getStringExtra(Notify.JOB));
                payload.put("ask_id", intent.getStringExtra(Notify.ASK));
                payload.put("yes", intent.getBooleanExtra(Notify.YES, false));
                int nid = intent.getIntExtra(Notify.NID, 0);
                if (nid != 0) {
                    getSystemService(NotificationManager.class).cancel(nid);
                }
            } else {
                payload.put("kind", intent.getStringExtra(Notify.KIND));
                payload.put("job", intent.getStringExtra(Notify.JOB));
                payload.put("id", intent.getStringExtra(Notify.ID));
            }
            deliver(answer ? "CapriAndroidAnswer" : "CapriAndroidOpen", payload.toString(),
                    SystemClock.uptimeMillis() + 15000);
        } catch (Exception e) {
            Log.w(TAG, "Notification tap not passed on: " + e.getClass().getSimpleName());
        }
        return true;
    }

    private void deliver(String function, String payload, long until) {
        String script = "(function(){var f=window." + function + ";if(typeof f!=='function')return false;f("
                + payload + ");return true})()";
        web.evaluateJavascript(script, value -> {
            if (!"true".equals(value) && SystemClock.uptimeMillis() < until && web != null) {
                web.postDelayed(() -> deliver(function, payload, until), 500);
            }
        });
    }

    @Override
    protected void onActivityResult(int code, int result, Intent data) {
        super.onActivityResult(code, result, data);
        if (code == FILE_REQUEST && pendingFile != null) {
            Uri[] picked = null;
            if (result == RESULT_OK && data != null) {
                if (data.getClipData() != null && data.getClipData().getItemCount() > 0) {
                    picked = new Uri[]{data.getClipData().getItemAt(0).getUri()};
                } else if (data.getData() != null) {
                    picked = new Uri[]{data.getData()};
                }
            }
            pendingFile.onReceiveValue(picked);
            pendingFile = null;
        }
    }

    private String startUrl(Intent intent) {
        String asked = intent == null ? null : intent.getStringExtra("url");
        Uri link = intent == null ? null : intent.getData();
        if (asked == null && link != null) {
            asked = link.toString();
        }
        if (asked != null && WebFiles.ours(Uri.parse(asked))) {
            return asked;
        }
        return HOME;
    }

    // What the page showing right now really is. The old flag was set in onPageStarted, so a document that navigated
    // inside our origin kept the bridge after it was no longer our page; this asks the WebView instead.
    private boolean onOurPage() {
        if (web == null) {
            return false;
        }
        final boolean[] ok = {false};
        final CountDownLatch done = new CountDownLatch(1);
        Runnable ask = () -> {
            try {
                String now = web.getUrl();
                ok[0] = now != null && WebFiles.ours(Uri.parse(now));
            } finally {
                done.countDown();
            }
        };
        if (Thread.currentThread() == getMainLooper().getThread()) {
            ask.run();
        } else {
            runOnUiThread(ask);
            try {
                done.await(500, TimeUnit.MILLISECONDS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }
        return ok[0];
    }

    private static String strip(String url) {
        int hash = url.indexOf('#');
        return hash < 0 ? url : url.substring(0, hash);
    }

    private void checkAr() {
        try {
            ArCoreApk.Availability availability = ArCoreApk.getInstance().checkAvailability(this);
            if (availability.isTransient()) {
                web.postDelayed(this::checkAr, 300);
            }
            arSupported = availability.isSupported();
        } catch (RuntimeException e) {
            arSupported = false;
        }
    }

    private SharedPreferences prefs() {
        return getSharedPreferences("capri", MODE_PRIVATE);
    }

    private boolean bubbleWanted() {
        return prefs().getBoolean("bubble", false);
    }

    private void grantMedia(PermissionRequest request) {
        List<String> allowed = new ArrayList<>();
        for (String resource : request.getResources()) {
            if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(resource)
                    && checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                allowed.add(resource);
            } else if (PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(resource)
                    && checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
                allowed.add(resource);
            }
        }
        if (allowed.isEmpty()) {
            request.deny();
        } else {
            request.grant(allowed.toArray(new String[0]));
        }
    }

    private void openOutside(Uri link) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, link).addCategory(Intent.CATEGORY_BROWSABLE));
        } catch (ActivityNotFoundException e) {
            Log.w(TAG, "No app opens " + link.getScheme());
        }
    }

    private String info() {
        try {
            JSONObject out = new JSONObject();
            out.put("version", BuildConfig.VERSION_NAME);
            out.put("ar", arSupported);
            out.put("bubble", bubbleWanted());
            out.put("overlay", Settings.canDrawOverlays(this));
            return out.toString();
        } catch (Exception e) {
            return "{}";
        }
    }

    private final class Client extends WebViewClient {
        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            return files.serve(request);
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri link = request.getUrl();
            if (WebFiles.ours(link)) {
                return false;
            }
            openOutside(link);
            return true;
        }

    }

    private final class Chrome extends WebChromeClient {
        @Override
        public void onPermissionRequest(PermissionRequest request) {
            runOnUiThread(() -> {
                if (!WebFiles.ours(request.getOrigin())) {
                    request.deny();
                    return;
                }
                List<String> needed = new ArrayList<>();
                for (String resource : request.getResources()) {
                    if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(resource)
                            && checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                        needed.add(Manifest.permission.CAMERA);
                    } else if (PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(resource)
                            && checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                        needed.add(Manifest.permission.RECORD_AUDIO);
                    }
                }
                if (needed.isEmpty()) {
                    grantMedia(request);
                } else {
                    if (pendingMedia != null) {
                        pendingMedia.deny();
                    }
                    pendingMedia = request;
                    requestPermissions(needed.toArray(new String[0]), MEDIA_REQUEST);
                }
            });
        }

        @Override
        public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
            // The picker hands a real file back to the page, so only open it for our own document.
            String page = view.getUrl();
            if (page == null || !WebFiles.ours(Uri.parse(page))) {
                callback.onReceiveValue(null);
                return false;
            }
            if (pendingFile != null) {
                pendingFile.onReceiveValue(null);
            }
            pendingFile = callback;
            Intent pick = new Intent(Intent.ACTION_GET_CONTENT).addCategory(Intent.CATEGORY_OPENABLE).setType("image/*");
            try {
                startActivityForResult(Intent.createChooser(pick, "Pick a picture"), FILE_REQUEST);
            } catch (ActivityNotFoundException e) {
                pendingFile = null;
                return false;
            }
            return true;
        }

        @Override
        public boolean onConsoleMessage(ConsoleMessage message) {
            Log.println(message.messageLevel() == ConsoleMessage.MessageLevel.ERROR ? Log.ERROR : Log.INFO,
                    "CapriWeb", message.message() + " (" + message.sourceId() + ":" + message.lineNumber() + ")");
            return true;
        }
    }

    private final class Bridge {
        @android.webkit.JavascriptInterface
        public String info() {
            return onOurPage() ? MainActivity.this.info() : "{}";
        }

        @android.webkit.JavascriptInterface
        public String bubble(boolean on) {
            if (!onOurPage()) {
                return "{}";
            }
            prefs().edit().putBoolean("bubble", on).apply();
            runOnUiThread(() -> {
                if (on && !Settings.canDrawOverlays(MainActivity.this)) {
                    try {
                        startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                                Uri.parse("package:" + getPackageName())));
                    } catch (ActivityNotFoundException e) {
                        Log.w(TAG, "No overlay settings page");
                    }
                } else if (on && Build.VERSION.SDK_INT >= 33
                        && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                    requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, NOTIFY_REQUEST);
                }
            });
            return MainActivity.this.info();
        }

        @android.webkit.JavascriptInterface
        public String notifyInfo() {
            return onOurPage() ? Notify.info(MainActivity.this) : "{\"on\":false,\"allowed\":false}";
        }

        @android.webkit.JavascriptInterface
        public String notifySet(boolean on) {
            if (!onOurPage()) {
                return "{\"on\":false,\"allowed\":false}";
            }
            Notify.wish(MainActivity.this, on);
            runOnUiThread(() -> {
                if (on && Build.VERSION.SDK_INT >= 33
                        && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                    requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, NOTIFY_REQUEST);
                }
            });
            return Notify.info(MainActivity.this);
        }

        @android.webkit.JavascriptInterface
        public boolean notify(String json) {
            return onOurPage() && Notify.show(MainActivity.this, json);
        }

        @android.webkit.JavascriptInterface
        public boolean room() {
            if (!onOurPage() || !arSupported) {
                return false;
            }
            runOnUiThread(() -> startActivity(new Intent(MainActivity.this, RoomActivity.class)));
            return true;
        }

        @android.webkit.JavascriptInterface
        public String wake(String cards) {
            if (!onOurPage() || cards == null || cards.length() > 4000) {
                return "{\"sent\":0}";
            }
            return "{\"sent\":" + Wake.send(cards) + "}";
        }

        @android.webkit.JavascriptInterface
        public void said(String text) {
            if (!onOurPage() || text == null || visible) {
                return;
            }
            String line = text.trim();
            if (line.isEmpty()) {
                return;
            }
            runOnUiThread(() -> BubbleService.say(line));
        }
    }
}
