package app.capriai.capri;

import android.content.Context;
import android.content.res.AssetManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.os.SystemClock;
import android.view.View;
import java.io.IOException;
import java.io.InputStream;

final class Sprite extends View {
    static final class Clip {
        final String name;
        final int count;
        final int columns;
        final int fps;
        final boolean loop;
        Bitmap sheet;
        int cellWidth;
        int cellHeight;

        Clip(String name, int count, int columns, int fps, boolean loop) {
            this.name = name;
            this.count = count;
            this.columns = columns;
            this.fps = fps;
            this.loop = loop;
        }
    }

    static final Clip IDLE = new Clip("idle", 16, 16, 8, true);
    static final Clip BLINK = new Clip("blink", 4, 4, 8, false);
    static final Clip GRIN = new Clip("grin", 16, 16, 8, false);
    static final Clip DANGLE = new Clip("dangle", 24, 17, 10, true);
    static final Clip SLEEP = new Clip("sleep", 16, 16, 6, true);
    static final Clip WAKE = new Clip("wake", 13, 13, 15, false);
    static final Clip[] ALL = {IDLE, BLINK, GRIN, DANGLE, SLEEP, WAKE};
    static final float ASPECT = 384f / 240f;

    private final Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
    private final Rect source = new Rect();
    private final Rect target = new Rect();
    private Clip clip;
    private long startedAt;
    private Runnable done;
    private final Runnable tick = this::tick;

    Sprite(Context context) {
        super(context);
        load(context.getAssets());
        play(IDLE, null);
    }

    private static void load(AssetManager assets) {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = 2;
        for (Clip each : ALL) {
            if (each.sheet != null) {
                continue;
            }
            try (InputStream in = assets.open("sprites/" + each.name + ".png")) {
                each.sheet = BitmapFactory.decodeStream(in, null, options);
                if (each.sheet != null) {
                    int rows = (each.count + each.columns - 1) / each.columns;
                    each.cellWidth = each.sheet.getWidth() / each.columns;
                    each.cellHeight = each.sheet.getHeight() / rows;
                }
            } catch (IOException e) {
                each.sheet = null;
            }
        }
    }

    Clip clip() {
        return clip;
    }

    void play(Clip next, Runnable then) {
        clip = next;
        done = then;
        startedAt = SystemClock.uptimeMillis();
        removeCallbacks(tick);
        post(tick);
    }

    private int frame() {
        long elapsed = SystemClock.uptimeMillis() - startedAt;
        int frame = (int) (elapsed * clip.fps / 1000);
        if (clip.loop) {
            return frame % clip.count;
        }
        return Math.min(frame, clip.count - 1);
    }

    private void tick() {
        invalidate();
        long elapsed = SystemClock.uptimeMillis() - startedAt;
        if (!clip.loop && elapsed * clip.fps / 1000 >= clip.count) {
            Runnable then = done;
            done = null;
            if (then != null) {
                then.run();
            } else {
                play(IDLE, null);
            }
            return;
        }
        postDelayed(tick, 1000 / clip.fps);
    }

    @Override
    protected void onDetachedFromWindow() {
        removeCallbacks(tick);
        super.onDetachedFromWindow();
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        removeCallbacks(tick);
        post(tick);
    }

    @Override
    protected void onDraw(Canvas canvas) {
        if (clip == null || clip.sheet == null) {
            return;
        }
        int frame = frame();
        int column = frame % clip.columns;
        int row = frame / clip.columns;
        source.set(column * clip.cellWidth, row * clip.cellHeight,
                (column + 1) * clip.cellWidth, (row + 1) * clip.cellHeight);
        target.set(0, 0, getWidth(), getHeight());
        canvas.drawBitmap(clip.sheet, source, target, paint);
    }
}
