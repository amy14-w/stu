package com.stu.face;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.ImageFormat;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Log;
import android.util.Size;
import android.view.Surface;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.Category;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.ImageProcessingOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.facelandmarker.FaceLandmarker;
import com.google.mediapipe.tasks.vision.facelandmarker.FaceLandmarkerResult;
import com.unity3d.player.UnityPlayer;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;

// Stu Step 1: opens ONLY the front camera with Camera2 (Unity's WebCamTexture keeps the back camera),
// runs MediaPipe FaceLandmarker (LIVE_STREAM, blendshapes) and sends a small JSON of numbers to Unity:
//   UnitySendMessage(receiver, "OnFace", json)       ~every processed frame (throttled by minFrameIntervalMs)
//   UnitySendMessage(receiver, "OnFaceError", text)  camera/model problems (e.g. front camera evicted)
// Privacy: frames never leave this class; only blendshape numbers are sent.
public class StuFaceTracker {
    private static final String TAG = "StuFace";
    private static final String TAG_ALL = "StuFaceAll"; // 1 Hz dump of all blendshapes for tuning
    private static final String MODEL_ASSET = "face_landmarker.task";

    private final Activity activity;
    private final String receiver;

    // Tunables (settable from C# before start()).
    public String cameraId = "1";          // S22: 1 = main front camera
    public int targetWidth = 640;
    public int targetHeight = 480;
    public int minFrameIntervalMs = 70;     // ~14 Hz max into MediaPipe
    public int rotationOverride = -1;       // -1 = compute from sensor + display rotation

    private HandlerThread camThread;
    private Handler camHandler;
    private CameraDevice camera;
    private CameraCaptureSession session;
    private ImageReader reader;
    private FaceLandmarker landmarker;

    private volatile boolean running;
    private volatile boolean busy;          // one frame in flight in MediaPipe at a time
    private long lastSubmitMs;
    private long lastTimestampMs;
    private long lastDumpMs;
    private int sensorOrientation;
    private Bitmap bitmap;
    private int[] argb;

    public StuFaceTracker(Activity activity, String receiver) {
        this.activity = activity;
        this.receiver = receiver;
    }

    public synchronized void start() {
        if (running) return;
        if (activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            sendError("camera permission not granted");
            return;
        }
        running = true;
        camThread = new HandlerThread("StuFaceCam");
        camThread.start();
        camHandler = new Handler(camThread.getLooper());
        camHandler.post(this::openEverything);
    }

    public synchronized void stop() {
        running = false;
        if (camHandler == null) return;
        final HandlerThread t = camThread;
        camHandler.post(() -> {
            closeEverything();
            t.quitSafely();
        });
        camHandler = null;
        camThread = null;
    }

    public boolean isRunning() {
        return running;
    }

    // ---------------------------------------------------------------- setup / teardown (camera thread)

    private void openEverything() {
        try {
            createLandmarker();
        } catch (Exception e) {
            Log.e(TAG, "FaceLandmarker init failed", e);
            sendError("model init failed: " + e.getMessage());
            running = false;
            return;
        }
        try {
            openCamera();
        } catch (Exception e) {
            Log.e(TAG, "openCamera failed", e);
            sendError("front camera open failed: " + e.getMessage());
            running = false;
        }
    }

    private void createLandmarker() throws Exception {
        // Load the model ourselves so it works even if the APK compressed the asset.
        ByteBuffer model;
        try (InputStream in = activity.getAssets().open(MODEL_ASSET)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream(4 << 20);
            byte[] buf = new byte[64 * 1024];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            byte[] bytes = out.toByteArray();
            model = ByteBuffer.allocateDirect(bytes.length);
            model.put(bytes);
            model.rewind();
        }

        FaceLandmarker.FaceLandmarkerOptions options = FaceLandmarker.FaceLandmarkerOptions.builder()
                .setBaseOptions(BaseOptions.builder().setModelAssetBuffer(model).build())
                .setRunningMode(RunningMode.LIVE_STREAM)
                .setNumFaces(1)
                .setOutputFaceBlendshapes(true)
                .setResultListener(this::onResult)
                .setErrorListener(e -> {
                    busy = false;
                    Log.e(TAG, "MediaPipe error", e);
                    sendError("mediapipe: " + e.getMessage());
                })
                .build();
        landmarker = FaceLandmarker.createFromOptions(activity, options);
        Log.i(TAG, "FaceLandmarker ready");
    }

    private void openCamera() throws CameraAccessException {
        CameraManager mgr = (CameraManager) activity.getSystemService(Activity.CAMERA_SERVICE);
        CameraCharacteristics cc = mgr.getCameraCharacteristics(cameraId);
        Integer so = cc.get(CameraCharacteristics.SENSOR_ORIENTATION);
        sensorOrientation = so != null ? so : 270;

        Size size = pickSize(cc.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP));
        Log.i(TAG, "front camera " + cameraId + " size " + size + " sensorOrientation " + sensorOrientation);

        reader = ImageReader.newInstance(size.getWidth(), size.getHeight(), ImageFormat.YUV_420_888, 2);
        reader.setOnImageAvailableListener(this::onImage, camHandler);

        mgr.openCamera(cameraId, new CameraDevice.StateCallback() {
            @Override public void onOpened(CameraDevice device) {
                camera = device;
                if (!running) { closeEverything(); return; }
                createSession();
            }
            @Override public void onDisconnected(CameraDevice device) {
                // This is the signal Step 1 is testing for: another client (e.g. WebCamTexture) took priority.
                Log.w(TAG, "front camera disconnected");
                sendError("front camera disconnected (evicted by another camera user?)");
                device.close();
                camera = null;
            }
            @Override public void onError(CameraDevice device, int error) {
                Log.e(TAG, "front camera error " + error);
                sendError("front camera error " + error);
                device.close();
                camera = null;
            }
        }, camHandler);
    }

    @SuppressWarnings("deprecation")
    private void createSession() {
        try {
            final Surface surface = reader.getSurface();
            camera.createCaptureSession(Arrays.asList(surface), new CameraCaptureSession.StateCallback() {
                @Override public void onConfigured(CameraCaptureSession s) {
                    session = s;
                    try {
                        CaptureRequest.Builder b = camera.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
                        b.addTarget(surface);
                        s.setRepeatingRequest(b.build(), null, camHandler);
                        Log.i(TAG, "front camera streaming");
                    } catch (Exception e) {
                        sendError("front repeating request failed: " + e.getMessage());
                    }
                }
                @Override public void onConfigureFailed(CameraCaptureSession s) {
                    sendError("front capture session configure failed");
                }
            }, camHandler);
        } catch (Exception e) {
            sendError("front createCaptureSession failed: " + e.getMessage());
        }
    }

    private Size pickSize(StreamConfigurationMap map) {
        Size best = new Size(targetWidth, targetHeight);
        if (map == null) return best;
        Size[] sizes = map.getOutputSizes(ImageFormat.YUV_420_888);
        if (sizes == null || sizes.length == 0) return best;
        long target = (long) targetWidth * targetHeight;
        long bestDiff = Long.MAX_VALUE;
        for (Size s : sizes) {
            long diff = Math.abs((long) s.getWidth() * s.getHeight() - target);
            if (diff < bestDiff) { bestDiff = diff; best = s; }
        }
        return best;
    }

    private void closeEverything() {
        try { if (session != null) session.close(); } catch (Exception ignored) {}
        try { if (camera != null) camera.close(); } catch (Exception ignored) {}
        try { if (reader != null) reader.close(); } catch (Exception ignored) {}
        try { if (landmarker != null) landmarker.close(); } catch (Exception ignored) {}
        session = null; camera = null; reader = null; landmarker = null;
        busy = false;
        Log.i(TAG, "stopped");
    }

    // ---------------------------------------------------------------- frames

    private void onImage(ImageReader r) {
        Image img = r.acquireLatestImage();
        if (img == null) return;
        try {
            long now = SystemClock.uptimeMillis();
            if (!running || busy || landmarker == null || now - lastSubmitMs < minFrameIntervalMs) return;

            int w = img.getWidth(), h = img.getHeight();
            if (bitmap == null || bitmap.getWidth() != w || bitmap.getHeight() != h) {
                bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
                argb = new int[w * h];
            }
            yuvToArgb(img, argb);
            bitmap.setPixels(argb, 0, w, 0, 0, w, h);

            // LIVE_STREAM needs strictly increasing timestamps.
            long ts = Math.max(now, lastTimestampMs + 1);
            lastTimestampMs = ts;
            lastSubmitMs = now;

            MPImage mp = new BitmapImageBuilder(bitmap).build();
            ImageProcessingOptions ipo = ImageProcessingOptions.builder()
                    .setRotationDegrees(currentRotation())
                    .build();
            busy = true;
            landmarker.detectAsync(mp, ipo, ts);
        } catch (Exception e) {
            busy = false;
            Log.e(TAG, "frame failed", e);
        } finally {
            img.close();
        }
    }

    // Rotation MediaPipe must apply to get an upright face (front camera).
    private int currentRotation() {
        if (rotationOverride >= 0) return rotationOverride;
        @SuppressWarnings("deprecation")
        int r = activity.getWindowManager().getDefaultDisplay().getRotation();
        int display = r == Surface.ROTATION_90 ? 90 : r == Surface.ROTATION_180 ? 180 : r == Surface.ROTATION_270 ? 270 : 0;
        return (sensorOrientation + display) % 360;
    }

    private static void yuvToArgb(Image img, int[] out) {
        int w = img.getWidth(), h = img.getHeight();
        Image.Plane[] p = img.getPlanes();
        ByteBuffer yB = p[0].getBuffer(), uB = p[1].getBuffer(), vB = p[2].getBuffer();
        int yRow = p[0].getRowStride(), yPix = p[0].getPixelStride();
        int uvRow = p[1].getRowStride(), uvPix = p[1].getPixelStride();
        int i = 0;
        for (int y = 0; y < h; y++) {
            int yBase = y * yRow;
            int uvBase = (y >> 1) * uvRow;
            for (int x = 0; x < w; x++) {
                int Y = (yB.get(yBase + x * yPix) & 0xff) - 16;
                int uvOff = uvBase + (x >> 1) * uvPix;
                int U = (uB.get(uvOff) & 0xff) - 128;
                int V = (vB.get(uvOff) & 0xff) - 128;
                if (Y < 0) Y = 0;
                int y1192 = 1192 * Y;
                int R = y1192 + 1634 * V;
                int G = y1192 - 833 * V - 400 * U;
                int B = y1192 + 2066 * U;
                R = R < 0 ? 0 : (R > 262143 ? 262143 : R);
                G = G < 0 ? 0 : (G > 262143 ? 262143 : G);
                B = B < 0 ? 0 : (B > 262143 ? 262143 : B);
                out[i++] = 0xff000000 | ((R << 6) & 0xff0000) | ((G >> 2) & 0xff00) | ((B >> 10) & 0xff);
            }
        }
    }

    // ---------------------------------------------------------------- results

    private void onResult(FaceLandmarkerResult result, MPImage input) {
        busy = false;
        if (!running) return;

        List<Category> shapes = null;
        if (result.faceBlendshapes().isPresent() && !result.faceBlendshapes().get().isEmpty())
            shapes = result.faceBlendshapes().get().get(0);

        StringBuilder sb = new StringBuilder(400);
        sb.append("{\"face\":").append(shapes != null);
        if (shapes != null) {
            for (Category c : shapes) {
                String key = jsonKey(c.categoryName());
                if (key != null)
                    sb.append(",\"").append(key).append("\":").append(String.format(Locale.US, "%.3f", c.score()));
            }
        }
        sb.append(",\"t\":").append(result.timestampMs()).append('}');
        if (shapes != null) dumpAll(shapes);
        UnityPlayer.UnitySendMessage(receiver, "OnFace", sb.toString());
    }

    // For tuning: `adb logcat -s StuFaceAll` shows every blendshape above 0.03 once per second.
    private void dumpAll(List<Category> shapes) {
        long now = SystemClock.uptimeMillis();
        if (now - lastDumpMs < 1000) return;
        lastDumpMs = now;
        StringBuilder sb = new StringBuilder(600);
        for (Category c : shapes)
            if (c.score() >= 0.03f)
                sb.append(c.categoryName()).append('=').append(String.format(Locale.US, "%.2f", c.score())).append(' ');
        Log.d(TAG_ALL, sb.toString());
    }

    // MediaPipe blendshape name -> short JSON key (only the ones Stu uses).
    private static String jsonKey(String name) {
        switch (name) {
            case "browDownLeft": return "browDownL";
            case "browDownRight": return "browDownR";
            case "browInnerUp": return "browInnerUp";
            case "eyeLookDownLeft": return "eyeLookDownL";
            case "eyeLookDownRight": return "eyeLookDownR";
            case "eyeLookUpLeft": return "eyeLookUpL";
            case "eyeLookUpRight": return "eyeLookUpR";
            case "eyeLookOutLeft": return "eyeLookOutL";
            case "eyeLookOutRight": return "eyeLookOutR";
            case "eyeBlinkLeft": return "eyeBlinkL";
            case "eyeBlinkRight": return "eyeBlinkR";
            case "eyeSquintLeft": return "eyeSquintL";
            case "eyeSquintRight": return "eyeSquintR";
            case "noseSneerLeft": return "noseSneerL";
            case "noseSneerRight": return "noseSneerR";
            case "mouthFrownLeft": return "mouthFrownL";
            case "mouthFrownRight": return "mouthFrownR";
            case "jawOpen": return "jawOpen";
            case "mouthSmileLeft": return "mouthSmileL";
            case "mouthSmileRight": return "mouthSmileR";
            default: return null;
        }
    }

    private void sendError(String msg) {
        UnityPlayer.UnitySendMessage(receiver, "OnFaceError", msg);
    }
}
