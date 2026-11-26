export const startCutout = async base => {
  const vision = await import(base + 'vision_bundle.js');
  const files = await vision.FilesetResolver.forVisionTasks(base + 'wasm');
  const make = delegate => vision.ImageSegmenter.createFromOptions(files, {
    baseOptions: { modelAssetPath: base + 'selfie_segmenter.tflite', delegate },
    runningMode: 'VIDEO',
    outputCategoryMask: false,
    outputConfidenceMasks: true
  });
  let segmenter;
  try { segmenter = await make('GPU'); } catch (error) { segmenter = await make('CPU'); }
  const plate = document.createElement('canvas');
  const pen = plate.getContext('2d', { willReadFrequently: true });
  let pixels = null, last = -1;
  const read = (video, now) => {
    if (!video.videoWidth || video.readyState < 2) return null;
    const at = Math.max(now, last + 1);
    let result;
    try { result = segmenter.segmentForVideo(video, at); } catch (error) { return null; }
    last = at;
    const mask = result?.confidenceMasks?.[0];
    if (!mask) { result?.close?.(); return null; }
    const width = mask.width, height = mask.height, values = mask.getAsFloat32Array();
    if (plate.width !== width || plate.height !== height) {
      plate.width = width;
      plate.height = height;
      pixels = pen.createImageData(width, height);
    }
    const data = pixels.data;
    for (let i = 0, n = width * height; i < n; i++) {
      const value = Math.max(0, Math.min(1, (values[i] - 0.25) / 0.5));
      data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = 255;
      data[i * 4 + 3] = value * 255;
    }
    result.close();
    pen.putImageData(pixels, 0, 0);
    return plate;
  };
  return { read, close: () => { try { segmenter.close(); } catch (error) {} } };
};
