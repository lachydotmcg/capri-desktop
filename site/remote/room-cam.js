let ticker = 0, canvas = null, pen = null;
const show = bitmap => {
  if (!canvas || canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
    canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    pen = canvas.getContext('bitmaprenderer') || canvas.getContext('2d');
  }
  if (pen.transferFromImageBitmap) pen.transferFromImageBitmap(bitmap);
  else { pen.drawImage(bitmap, 0, 0); bitmap.close(); }
};
onmessage = async ({ data }) => {
  if (data.every) {
    clearInterval(ticker);
    ticker = setInterval(() => postMessage({ tick: true }), data.every);
    return;
  }
  if (!data.bitmap) return;
  try {
    show(data.bitmap);
    const blob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.85 });
    await fetch(data.to, { method: 'POST', body: blob, headers: { 'content-type': 'image/jpeg' } });
  } catch (error) {}
  postMessage({ sent: true });
};
