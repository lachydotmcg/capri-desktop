package app.capriai.capri;

import static android.opengl.GLES11Ext.GL_TEXTURE_EXTERNAL_OES;
import static android.opengl.GLES20.*;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Rect;
import android.opengl.GLSurfaceView;
import android.opengl.GLUtils;
import android.opengl.Matrix;
import android.os.SystemClock;
import android.util.Log;
import com.google.ar.core.Anchor;
import com.google.ar.core.Camera;
import com.google.ar.core.Coordinates2d;
import com.google.ar.core.Frame;
import com.google.ar.core.HitResult;
import com.google.ar.core.Plane;
import com.google.ar.core.Pose;
import com.google.ar.core.Session;
import com.google.ar.core.TrackingState;
import com.google.ar.core.exceptions.CameraNotAvailableException;
import com.google.ar.core.exceptions.NotTrackingException;
import com.google.ar.core.exceptions.ResourceExhaustedException;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import javax.microedition.khronos.egl.EGLConfig;
import javax.microedition.khronos.opengles.GL10;

final class RoomRenderer implements GLSurfaceView.Renderer {
    enum Placement { SCANNING, READY, MISSED, PLACED, MOVE_MISSED, LOST }

    private static final String TAG = "CapriRoom";
    private static final int CELL_WIDTH = 240;
    private static final int CELL_HEIGHT = 384;
    private static final int ATLAS_COLUMNS = 4;
    private static final int FRAME_COUNT = 16;
    private static final int ATLAS_WIDTH = CELL_WIDTH * ATLAS_COLUMNS;
    private static final int ATLAS_HEIGHT = CELL_HEIGHT * FRAME_COUNT / ATLAS_COLUMNS;
    private static final float PET_HEIGHT = 0.55f;
    private static final float PET_HALF_WIDTH = PET_HEIGHT * CELL_WIDTH / CELL_HEIGHT / 2f;
    private static final float PET_BOTTOM = -PET_HEIGHT * 0.0547f;

    private static final class Tap {
        final float x;
        final float y;

        Tap(float x, float y) {
            this.x = x;
            this.y = y;
        }
    }

    private static final Tap CLEAR = new Tap(0, 0);
    private final RoomActivity activity;
    private final Consumer<Placement> status;
    private final Consumer<Boolean> failure;
    private final AtomicReference<Tap> pendingTap = new AtomicReference<>();
    private final FloatBuffer cameraQuad = buffer(-1, -1, 1, -1, -1, 1, 1, 1);
    private final FloatBuffer cameraUv = buffer(0, 0, 0, 0, 0, 0, 0, 0);
    private final FloatBuffer petQuad = buffer(
            -PET_HALF_WIDTH, PET_BOTTOM, PET_HALF_WIDTH, PET_BOTTOM,
            -PET_HALF_WIDTH, PET_BOTTOM + PET_HEIGHT, PET_HALF_WIDTH, PET_BOTTOM + PET_HEIGHT);
    private final FloatBuffer petUv = buffer(0, 1, 1, 1, 0, 0, 1, 0);
    private final float[] model = new float[16];
    private final float[] view = new float[16];
    private final float[] projection = new float[16];
    private final float[] modelView = new float[16];
    private final float[] modelViewProjection = new float[16];

    private Session session;
    private Session boundSession;
    private Anchor anchor;
    private int vertexBuffer;
    private int cameraTexture;
    private int petTexture;
    private int cameraProgram;
    private int petProgram;
    private int petMatrixUniform;
    private int atlasUniform;
    private int width;
    private int height;
    private int rotation = -1;
    private boolean geometryDirty = true;
    private boolean initialized;
    private boolean failed;
    private Placement currentPlacement;
    private long missedUntil;
    private long nextPlaneCheck;
    private boolean hasPlane;
    private long placedAt;
    private int callbackGeneration;

    RoomRenderer(RoomActivity activity, Consumer<Placement> status, Consumer<Boolean> failure) {
        this.activity = activity;
        this.status = status;
        this.failure = failure;
    }

    void attach(Session nextSession) {
        invalidateCallbacks();
        if (session != nextSession) {
            anchor = null;
            boundSession = null;
            geometryDirty = true;
            hasPlane = false;
            missedUntil = 0;
            nextPlaneCheck = 0;
        }

        session = nextSession;
        pendingTap.set(null);
        failed = false;
        currentPlacement = null;
    }

    void invalidateCallbacks() {
        callbackGeneration++;
    }

    void tap(float x, float y) {
        pendingTap.set(new Tap(x, y));
    }

    void clear() {
        pendingTap.set(CLEAR);
    }

    @Override
    public void onSurfaceCreated(GL10 unused, EGLConfig config) {
        initialized = false;
        geometryDirty = true;
        boundSession = null;
        glClearColor(0.07f, 0.08f, 0.10f, 1f);

        try {
            cameraProgram = program("""
                    #extension GL_OES_EGL_image_external : require
                    precision mediump float;
                    varying mediump vec2 uv;
                    uniform samplerExternalOES image;
                    void main() { gl_FragColor = texture2D(image, uv); }
                    """, false);
            petProgram = program("""
                    precision mediump float;
                    varying mediump vec2 uv;
                    uniform vec4 atlas;
                    uniform sampler2D image;
                    void main() { gl_FragColor = texture2D(image, atlas.xy + uv * atlas.zw); }
                    """, true);
            int[] vertexBuffers = new int[1];
            glGenBuffers(1, vertexBuffers, 0);
            vertexBuffer = vertexBuffers[0];
            glBindBuffer(GL_ARRAY_BUFFER, vertexBuffer);
            glBufferData(GL_ARRAY_BUFFER, 16 * Float.BYTES, null, GL_STREAM_DRAW);
            petMatrixUniform = glGetUniformLocation(petProgram, "matrix");
            atlasUniform = glGetUniformLocation(petProgram, "atlas");
            cameraTexture = texture(GL_TEXTURE_EXTERNAL_OES);
            petTexture = texture(GL_TEXTURE_2D);
            loadSprite();
            initialized = true;
        } catch (IOException | RuntimeException e) {
            Log.e(TAG, "Could not initialize AR graphics", e);
        }
    }

    @Override
    public void onSurfaceChanged(GL10 unused, int width, int height) {
        this.width = width;
        this.height = height;
        geometryDirty = true;
        glViewport(0, 0, width, height);
    }

    @Override
    public void onDrawFrame(GL10 unused) {
        glClear(GL_COLOR_BUFFER_BIT);
        if (session == null || width <= 0 || height <= 0 || failed) {
            return;
        }
        if (!initialized) {
            reportFailure(false);
            return;
        }

        try {
            drawFrame();
        } catch (CameraNotAvailableException e) {
            Log.e(TAG, "AR camera failed", e);
            reportFailure(true);
        } catch (RuntimeException e) {
            Log.e(TAG, "Could not render AR scene", e);
            reportFailure(false);
        }
    }

    private void drawFrame() throws CameraNotAvailableException {
        if (boundSession != session) {
            session.setCameraTextureName(cameraTexture);
            boundSession = session;
        }
        int displayRotation = activity.displayRotation();
        boolean changed = geometryDirty || rotation != displayRotation;
        if (changed) {
            session.setDisplayGeometry(displayRotation, width, height);
            rotation = displayRotation;
            geometryDirty = false;
        }

        Frame frame = session.update();
        if (changed || frame.hasDisplayGeometryChanged()) {
            cameraQuad.position(0);
            cameraUv.position(0);
            frame.transformCoordinates2d(Coordinates2d.OPENGL_NORMALIZED_DEVICE_COORDINATES,
                    cameraQuad, Coordinates2d.TEXTURE_NORMALIZED, cameraUv);
        }
        if (frame.getTimestamp() != 0) {
            drawQuad(cameraProgram, GL_TEXTURE_EXTERNAL_OES, cameraTexture, cameraQuad, cameraUv);
        }

        Camera camera = frame.getCamera();
        long now = SystemClock.uptimeMillis();
        Tap tap = pendingTap.getAndSet(null);
        if (tap == CLEAR) {
            removeAnchor();
            missedUntil = 0;
        }

        if (camera.getTrackingState() != TrackingState.TRACKING) {
            report(anchor == null ? Placement.SCANNING : Placement.LOST);
            return;
        }

        if (tap != null && tap != CLEAR) {
            place(frame, tap, now);
        }

        if (anchor != null) {
            if (anchor.getTrackingState() == TrackingState.TRACKING) {
                drawPet(camera, now);
                report(now < missedUntil ? Placement.MOVE_MISSED : Placement.PLACED);
                return;
            }
            if (anchor.getTrackingState() == TrackingState.PAUSED) {
                report(Placement.LOST);
                return;
            }
            removeAnchor();
        }

        if (now >= nextPlaneCheck) {
            hasPlane = session.getAllTrackables(Plane.class).stream()
                    .anyMatch(plane -> plane.getTrackingState() == TrackingState.TRACKING
                            && plane.getType() == Plane.Type.HORIZONTAL_UPWARD_FACING
                            && plane.getSubsumedBy() == null);
            nextPlaneCheck = now + 300;
        }
        if (now < missedUntil) {
            report(Placement.MISSED);
        } else {
            report(hasPlane ? Placement.READY : Placement.SCANNING);
        }
    }

    private void place(Frame frame, Tap tap, long now) {
        for (HitResult hit : frame.hitTest(tap.x, tap.y)) {
            if (hit.getTrackable() instanceof Plane plane
                    && plane.getTrackingState() == TrackingState.TRACKING
                    && plane.getType() == Plane.Type.HORIZONTAL_UPWARD_FACING
                    && plane.isPoseInPolygon(hit.getHitPose())) {
                Anchor nextAnchor;
                try {
                    nextAnchor = hit.createAnchor();
                } catch (NotTrackingException | ResourceExhaustedException e) {
                    break;
                }
                removeAnchor();
                anchor = nextAnchor;
                placedAt = now;
                missedUntil = 0;
                return;
            }
        }
        missedUntil = now + 2400;
    }

    private void removeAnchor() {
        if (anchor != null) {
            anchor.detach();
            anchor = null;
        }
    }

    private void drawPet(Camera camera, long now) {
        Pose position = anchor.getPose();
        Pose eye = camera.getPose();
        float yaw = (float) Math.toDegrees(Math.atan2(
                eye.tx() - position.tx(), eye.tz() - position.tz()));
        Matrix.setIdentityM(model, 0);
        Matrix.translateM(model, 0, position.tx(), position.ty(), position.tz());
        Matrix.rotateM(model, 0, yaw, 0f, 1f, 0f);

        camera.getViewMatrix(view, 0);
        camera.getProjectionMatrix(projection, 0, 0.05f, 30f);
        Matrix.multiplyMM(modelView, 0, view, 0, model, 0);
        Matrix.multiplyMM(modelViewProjection, 0, projection, 0, modelView, 0);

        int cell = (int) ((now - placedAt) / 125 % FRAME_COUNT);
        glUseProgram(petProgram);
        glUniformMatrix4fv(petMatrixUniform, 1, false, modelViewProjection, 0);
        glUniform4f(atlasUniform,
                (cell % ATLAS_COLUMNS * CELL_WIDTH + 0.5f) / ATLAS_WIDTH,
                (cell / ATLAS_COLUMNS * CELL_HEIGHT + 0.5f) / ATLAS_HEIGHT,
                (CELL_WIDTH - 1f) / ATLAS_WIDTH,
                (CELL_HEIGHT - 1f) / ATLAS_HEIGHT);

        glEnable(GL_BLEND);
        glBlendFunc(GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
        drawQuad(petProgram, GL_TEXTURE_2D, petTexture, petQuad, petUv);
        glDisable(GL_BLEND);
    }

    private void report(Placement placement) {
        if (currentPlacement != placement) {
            currentPlacement = placement;
            int generation = callbackGeneration;
            activity.runOnUiThread(() -> {
                if (generation == callbackGeneration) {
                    status.accept(placement);
                }
            });
        }
    }

    private void reportFailure(boolean cameraUnavailable) {
        failed = true;
        int generation = callbackGeneration;
        activity.runOnUiThread(() -> {
            if (generation == callbackGeneration) {
                failure.accept(cameraUnavailable);
            }
        });
    }

    private void loadSprite() throws IOException {
        Bitmap sheet;
        try (InputStream asset = activity.getAssets().open("sprites/idle.png")) {
            sheet = BitmapFactory.decodeStream(asset);
        }
        if (sheet == null || sheet.getWidth() != CELL_WIDTH * FRAME_COUNT
                || sheet.getHeight() != CELL_HEIGHT) {
            if (sheet != null) {
                sheet.recycle();
            }
            throw new IOException("Capri idle sheet has unexpected dimensions");
        }

        Bitmap atlas = Bitmap.createBitmap(ATLAS_WIDTH, ATLAS_HEIGHT, Bitmap.Config.ARGB_8888);
        try {
            Canvas canvas = new Canvas(atlas);
            for (int cell = 0; cell < FRAME_COUNT; cell++) {
                int sourceX = cell * CELL_WIDTH;
                int x = cell % ATLAS_COLUMNS * CELL_WIDTH;
                int y = cell / ATLAS_COLUMNS * CELL_HEIGHT;
                canvas.drawBitmap(sheet,
                        new Rect(sourceX, 0, sourceX + CELL_WIDTH, CELL_HEIGHT),
                        new Rect(x, y, x + CELL_WIDTH, y + CELL_HEIGHT), null);
            }
            glBindTexture(GL_TEXTURE_2D, petTexture);
            GLUtils.texImage2D(GL_TEXTURE_2D, 0, atlas, 0);
            int error = glGetError();
            if (error != GL_NO_ERROR) {
                throw new IOException("Could not upload Capri sprite: " + error);
            }
        } finally {
            sheet.recycle();
            atlas.recycle();
        }
    }

    private void drawQuad(int program, int target, int texture,
                                 FloatBuffer positions, FloatBuffer coordinates) {
        glUseProgram(program);
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(target, texture);
        glEnableVertexAttribArray(0);
        glEnableVertexAttribArray(1);
        positions.position(0);
        coordinates.position(0);
        glBindBuffer(GL_ARRAY_BUFFER, vertexBuffer);
        glBufferSubData(GL_ARRAY_BUFFER, 0, 8 * Float.BYTES, positions);
        glBufferSubData(GL_ARRAY_BUFFER, 8 * Float.BYTES, 8 * Float.BYTES, coordinates);
        glVertexAttribPointer(0, 2, GL_FLOAT, false, 0, 0);
        glVertexAttribPointer(1, 2, GL_FLOAT, false, 0, 8 * Float.BYTES);
        glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        glDisableVertexAttribArray(0);
        glDisableVertexAttribArray(1);
    }

    private static int texture(int target) {
        int[] texture = new int[1];
        glGenTextures(1, texture, 0);
        glBindTexture(target, texture[0]);
        glTexParameteri(target, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(target, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        glTexParameteri(target, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(target, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        return texture[0];
    }

    private static int program(String fragmentSource, boolean useMatrix) {
        String vertexSource = String.format(Locale.ROOT, """
                attribute vec2 position;
                attribute vec2 coordinates;
                uniform mat4 matrix;
                varying mediump vec2 uv;
                void main() {
                    uv = coordinates;
                    gl_Position = %s;
                }
                """, useMatrix ? "matrix * vec4(position, 0.0, 1.0)"
                        : "vec4(position, 0.0, 1.0)");

        int vertex = shader(GL_VERTEX_SHADER, vertexSource);
        int fragment = shader(GL_FRAGMENT_SHADER, fragmentSource);
        int program = glCreateProgram();
        glAttachShader(program, vertex);
        glAttachShader(program, fragment);
        glBindAttribLocation(program, 0, "position");
        glBindAttribLocation(program, 1, "coordinates");
        glLinkProgram(program);
        glDeleteShader(vertex);
        glDeleteShader(fragment);

        int[] linked = new int[1];
        glGetProgramiv(program, GL_LINK_STATUS, linked, 0);
        if (linked[0] == 0) {
            String log = glGetProgramInfoLog(program);
            glDeleteProgram(program);
            throw new IllegalStateException(log);
        }

        glUseProgram(program);
        glUniform1i(glGetUniformLocation(program, "image"), 0);
        return program;
    }

    private static int shader(int type, String source) {
        int shader = glCreateShader(type);
        glShaderSource(shader, source);
        glCompileShader(shader);
        int[] compiled = new int[1];
        glGetShaderiv(shader, GL_COMPILE_STATUS, compiled, 0);
        if (compiled[0] == 0) {
            String log = glGetShaderInfoLog(shader);
            glDeleteShader(shader);
            throw new IllegalStateException(log);
        }
        return shader;
    }

    private static FloatBuffer buffer(float... values) {
        FloatBuffer result = ByteBuffer.allocateDirect(values.length * Float.BYTES)
                .order(ByteOrder.nativeOrder()).asFloatBuffer();
        result.put(values);
        result.position(0);
        return result;
    }
}
