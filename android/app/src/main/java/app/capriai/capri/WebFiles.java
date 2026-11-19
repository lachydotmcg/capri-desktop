package app.capriai.capri;

import android.content.res.AssetManager;
import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import java.io.IOException;
import java.io.InputStream;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

final class WebFiles {
    static final String HOST = "capriai.app";
    private static final String[] SERVED = {"/remote/", "/assets/sprites/", "/assets/fonts/"};
    private static final Map<String, String> TYPES = new HashMap<>();

    static {
        TYPES.put("html", "text/html");
        TYPES.put("js", "text/javascript");
        TYPES.put("mjs", "text/javascript");
        TYPES.put("css", "text/css");
        TYPES.put("json", "application/json");
        TYPES.put("webmanifest", "application/manifest+json");
        TYPES.put("png", "image/png");
        TYPES.put("webp", "image/webp");
        TYPES.put("jpg", "image/jpeg");
        TYPES.put("svg", "image/svg+xml");
        TYPES.put("ttf", "font/ttf");
        TYPES.put("woff2", "font/woff2");
        TYPES.put("woff", "font/woff");
        TYPES.put("wasm", "application/wasm");
        TYPES.put("glb", "model/gltf-binary");
    }

    private final AssetManager assets;

    WebFiles(AssetManager assets) {
        this.assets = assets;
    }

    static boolean ours(Uri link) {
        if (link == null || !"https".equals(link.getScheme()) || !HOST.equals(link.getHost())) {
            return false;
        }
        if (link.getPort() != -1 && link.getPort() != 443) {
            return false;
        }
        String path = link.getPath();
        return path != null && (path.equals("/remote") || path.startsWith("/remote/"));
    }

    static String assetFor(Uri link) {
        if (link == null || !"https".equals(link.getScheme()) || !HOST.equals(link.getHost())) {
            return null;
        }
        String path = link.getPath();
        if (path == null) {
            return null;
        }
        if (path.equals("/remote")) {
            path = "/remote/";
        }
        boolean served = false;
        for (String prefix : SERVED) {
            served |= path.startsWith(prefix);
        }
        if (!served) {
            return null;
        }
        for (String part : path.split("/")) {
            if (part.equals("..") || part.equals(".") || part.contains("\\")) {
                return null;
            }
        }
        if (path.endsWith("/")) {
            path += "index.html";
        }
        return "web" + path;
    }

    static String typeOf(String name) {
        int dot = name.lastIndexOf('.');
        String type = dot < 0 ? null : TYPES.get(name.substring(dot + 1).toLowerCase(Locale.ROOT));
        return type == null ? "application/octet-stream" : type;
    }

    WebResourceResponse serve(WebResourceRequest request) {
        if (!"GET".equalsIgnoreCase(request.getMethod())) {
            return null;
        }
        String name = assetFor(request.getUrl());
        if (name == null) {
            return null;
        }
        Map<String, String> headers = new HashMap<>();
        headers.put("Cache-Control", "no-cache");
        headers.put("X-Content-Type-Options", "nosniff");
        String type = typeOf(name);
        String charset = type.startsWith("text/") || type.endsWith("json") ? "utf-8" : null;
        try {
            InputStream body = assets.open(name, AssetManager.ACCESS_STREAMING);
            return new WebResourceResponse(type, charset, 200, "OK", headers, body);
        } catch (IOException e) {
            return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", headers,
                    new java.io.ByteArrayInputStream(new byte[0]));
        }
    }
}
