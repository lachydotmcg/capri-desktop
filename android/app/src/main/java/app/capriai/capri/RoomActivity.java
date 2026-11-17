package app.capriai.capri;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.opengl.GLSurfaceView;
import android.os.Bundle;
import android.provider.Settings;
import android.text.Html;
import android.text.method.LinkMovementMethod;
import android.util.Log;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import com.google.ar.core.ArCoreApk;
import com.google.ar.core.Config;
import com.google.ar.core.Session;
import com.google.ar.core.exceptions.CameraNotAvailableException;
import com.google.ar.core.exceptions.UnavailableApkTooOldException;
import com.google.ar.core.exceptions.UnavailableArcoreNotInstalledException;
import com.google.ar.core.exceptions.UnavailableDeviceNotCompatibleException;
import com.google.ar.core.exceptions.UnavailableSdkTooOldException;
import com.google.ar.core.exceptions.UnavailableUserDeclinedInstallationException;

public final class RoomActivity extends Activity {
    private static final int CAMERA_REQUEST = 4;
    private static final String TAG = "CapriRoom";

    private GLSurfaceView surface;
    private RoomRenderer renderer;
    private TextView status;
    private TextView reticle;
    private Button action;
    private Session session;
    private boolean running;
    private boolean installRequested;
    private boolean requestingCamera;
    private boolean active;
    private boolean sessionClosing;
    private boolean restartAfterClose;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        FrameLayout room = new FrameLayout(this);
        room.setBackgroundColor(Color.rgb(18, 21, 25));
        surface = new GLSurfaceView(this);
        surface.setEGLContextClientVersion(2);
        surface.setPreserveEGLContextOnPause(true);
        renderer = new RoomRenderer(this, this::showTracking, this::handleRendererFailure);
        surface.setRenderer(renderer);
        surface.onPause();
        surface.setContentDescription("Room camera");
        surface.setOnTouchListener((view, event) -> {
            if (event.getActionMasked() == MotionEvent.ACTION_UP && running) {
                renderer.tap(event.getX(), event.getY());
                view.performClick();
            }
            return true;
        });
        room.addView(surface, new FrameLayout.LayoutParams(-1, -1));

        reticle = new TextView(this);
        reticle.setText("+");
        reticle.setTextColor(Color.WHITE);
        reticle.setTextSize(30);
        reticle.setGravity(Gravity.CENTER);
        reticle.setShadowLayer(4, 0, 1, Color.BLACK);
        reticle.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        reticle.setVisibility(View.GONE);
        room.addView(reticle, new FrameLayout.LayoutParams(dp(48), dp(48), Gravity.CENTER));

        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(18), dp(13), dp(18), dp(13));
        GradientDrawable background = new GradientDrawable();
        background.setColor(0xE6121519);
        background.setCornerRadius(dp(20));
        panel.setBackground(background);

        TextView title = new TextView(this);
        title.setText("Capri Room");
        title.setTextColor(0xFF9EF2DF);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setTextSize(14);
        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        Button about = new Button(this, null, android.R.attr.borderlessButtonStyle);
        about.setText("About");
        about.setTextColor(0xFF9EF2DF);
        about.setOnClickListener(view -> showAbout());
        header.addView(about, new LinearLayout.LayoutParams(-2, -2));
        panel.addView(header);

        status = new TextView(this);
        status.setTextColor(Color.WHITE);
        status.setTextSize(16);
        status.setPadding(0, dp(4), 0, dp(2));
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        panel.addView(status);

        action = new Button(this);
        action.setVisibility(View.GONE);
        panel.addView(action, new LinearLayout.LayoutParams(-2, -2));

        FrameLayout.LayoutParams panelLayout = new FrameLayout.LayoutParams(-1, -2, Gravity.BOTTOM);
        panelLayout.setMargins(dp(16), dp(16), dp(16), dp(20));
        room.addView(panel, panelLayout);
        setContentView(room);

        if (state != null) {
            installRequested = state.getBoolean("installRequested");
            requestingCamera = state.getBoolean("requestingCamera");
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        active = true;
        startRoom();
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        state.putBoolean("installRequested", installRequested);
        state.putBoolean("requestingCamera", requestingCamera);
        super.onSaveInstanceState(state);
    }

    @Override
    protected void onPause() {
        active = false;
        stopRoom();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        closeSession();
        super.onDestroy();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode == CAMERA_REQUEST) {
            requestingCamera = false;
            if (active) {
                startRoom();
            }
        }
    }

    private void startRoom() {
        if (running) {
            return;
        }
        if (sessionClosing) {
            restartAfterClose = true;
            showMessage("Restarting the AR camera…", null, null);
            return;
        }
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            showMessage("Allow camera access to place Capri in your room.", "Allow camera", () -> {
                if (getPreferences(MODE_PRIVATE).getBoolean("cameraAsked", false)
                        && !shouldShowRequestPermissionRationale(Manifest.permission.CAMERA)) {
                    Intent settings = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
                    settings.setData(Uri.fromParts("package", getPackageName(), null));
                    startActivity(settings);
                } else if (!requestingCamera) {
                    requestingCamera = true;
                    getPreferences(MODE_PRIVATE).edit().putBoolean("cameraAsked", true).apply();
                    requestPermissions(new String[]{Manifest.permission.CAMERA}, CAMERA_REQUEST);
                }
            });
            return;
        }

        try {
            if (session == null) {
                ArCoreApk.InstallStatus installation =
                        ArCoreApk.getInstance().requestInstall(this, !installRequested);
                if (installation == ArCoreApk.InstallStatus.INSTALL_REQUESTED) {
                    installRequested = true;
                    showMessage("Finish setting up Google Play Services for AR.", null, null);
                    return;
                }
                session = new Session(this);
                Config config = new Config(session);
                config.setPlaneFindingMode(Config.PlaneFindingMode.HORIZONTAL);
                config.setUpdateMode(Config.UpdateMode.LATEST_CAMERA_IMAGE);
                session.configure(config);
            }

            session.resume();
            renderer.attach(session);
            running = true;
            showMessage("Move your phone slowly to scan the room.", null, null);
            surface.onResume();
        } catch (UnavailableDeviceNotCompatibleException e) {
            closeSession();
            showMessage("This phone doesn’t support Google ARCore.", null, null);
        } catch (UnavailableArcoreNotInstalledException | UnavailableApkTooOldException |
                 UnavailableUserDeclinedInstallationException e) {
            closeSession();
            showMessage("Install or update Google Play Services for AR.", "Try again", () -> {
                installRequested = false;
                startRoom();
            });
        } catch (UnavailableSdkTooOldException e) {
            closeSession();
            showMessage("This phone needs a newer version of Capri.", null, null);
        } catch (CameraNotAvailableException | RuntimeException e) {
            Log.e(TAG, "Could not start AR", e);
            closeSession();
            showMessage("Couldn’t start the AR camera.", "Try again", this::startRoom);
        } catch (Exception e) {
            Log.e(TAG, "Could not initialize AR", e);
            closeSession();
            showMessage("Couldn’t start Google ARCore.", "Try again", this::startRoom);
        }
    }

    private void stopRoom() {
        if (running) {
            running = false;
            surface.onPause();
            renderer.invalidateCallbacks();
            session.pause();
        }
    }

    private void closeSession() {
        stopRoom();
        if (session != null) {
            renderer.attach(null);
            Session previous = session;
            session = null;
            sessionClosing = true;
            new Thread(() -> {
                try {
                    previous.close();
                } catch (RuntimeException e) {
                    Log.e(TAG, "Could not close Google ARCore", e);
                } finally {
                    runOnUiThread(() -> {
                        sessionClosing = false;
                        if (restartAfterClose) {
                            restartAfterClose = false;
                            if (active) {
                                startRoom();
                            }
                        }
                    });
                }
            }, "CapriSessionClose").start();
        }
    }

    private void handleRendererFailure(boolean cameraUnavailable) {
        if (!running) {
            return;
        }

        closeSession();
        if (cameraUnavailable) {
            showMessage("The camera stopped. Try again after closing other camera apps.",
                    "Try again", this::startRoom);
        } else {
            showMessage("Capri Room couldn’t display the AR scene.", null, null);
        }
    }

    private void showTracking(RoomRenderer.Placement state) {
        if (!running) {
            return;
        }

        switch (state) {
            case SCANNING:
                showMessage("Move your phone slowly over a floor or table.", null, null);
                break;
            case READY:
                showMessage("Aim at the floor or table and tap to place Capri.", null, null);
                reticle.setVisibility(View.VISIBLE);
                break;
            case MISSED:
                showMessage("That spot hasn’t been scanned. Try a nearby surface.", null, null);
                reticle.setVisibility(View.VISIBLE);
                break;
            case PLACED:
                showMessage("Tap another spot to move Capri.", "Remove Capri", renderer::clear);
                break;
            case MOVE_MISSED:
                showMessage("That spot hasn’t been scanned. Try a nearby surface.",
                        "Remove Capri", renderer::clear);
                break;
            case LOST:
                showMessage("Point your phone back at Capri’s spot to restore tracking.",
                        "Remove Capri", renderer::clear);
                break;
        }
    }

    private void showMessage(String message, String label, Runnable callback) {
        status.setText(message);
        reticle.setVisibility(View.GONE);
        action.setVisibility(callback == null ? View.GONE : View.VISIBLE);
        action.setText(label);
        action.setOnClickListener(callback == null ? null : view -> callback.run());
    }

    private void showAbout() {
        TextView detail = new TextView(this);
        detail.setPadding(dp(24), dp(6), dp(24), dp(12));
        detail.setTextSize(16);
        detail.setText(Html.fromHtml("""
                <p>Capri Room uses <a href="https://play.google.com/store/apps/details?id=com.google.ar.core">Google Play Services for AR</a>
                (ARCore), provided by Google and governed by the
                <a href="https://policies.google.com/privacy">Google Privacy Policy</a>.</p>
                <p>Art and animation by Capri's artist.
                <a href="https://github.com/lachydotmcg/capri-desktop">Capri project</a>, copyright 2026 Lachy.
                <a href="https://polyformproject.org/licenses/noncommercial/1.0.0">PolyForm Noncommercial License 1.0.0</a>.</p>
                """, Html.FROM_HTML_MODE_LEGACY));
        detail.setMovementMethod(LinkMovementMethod.getInstance());
        new AlertDialog.Builder(this)
                .setTitle("About Capri Room")
                .setView(detail)
                .setPositiveButton("Close", null)
                .show();
    }

    int displayRotation() {
        return getWindowManager().getDefaultDisplay().getRotation();
    }

    private int dp(int units) {
        return Math.round(units * getResources().getDisplayMetrics().density);
    }
}
