(() => {
  const repo = 'lachydotmcg/capri-desktop';
  // Buttons that count as a download: the Microsoft Store's and the installer's. Only the installer's follow each release.
  const links = [...document.querySelectorAll('.download[data-api]')];
  const installers = links.filter(link => link.href.includes('/releases/'));
  const point = (release) => {
    const asset = release && !release.draft && !release.prerelease && Array.isArray(release.assets) && release.assets.find(item => item.name === 'Capri-Setup.exe' && item.size > 1024 && typeof item.browser_download_url === 'string' && item.browser_download_url.startsWith('https://github.com/' + repo + '/releases/download/'));
    if (!asset) throw new Error('Installer unavailable');
    for (const link of installers) link.href = asset.browser_download_url;
  };
  const readRelease = async (url) => {
    const response = await fetch(url, { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Release unavailable');
    point(await response.json());
  };
  readRelease('https://api.github.com/repos/' + repo + '/releases/latest').catch(() => readRelease('../release.json')).catch(() => {});
  for (const link of links) link.addEventListener('click', () => { fetch(link.dataset.api, { method: 'POST', keepalive: true }).catch(() => {}); });

  for (const button of document.querySelectorAll('.copy')) {
    button.addEventListener('click', async () => {
      const text = document.getElementById(button.dataset.copy);
      try {
        await navigator.clipboard.writeText(text.textContent.trim());
        button.textContent = 'Copied';
      } catch {
        getSelection().selectAllChildren(text);
        button.textContent = 'Press Ctrl + C';
      }
      clearTimeout(button.reset);
      button.reset = setTimeout(() => { button.textContent = 'Copy'; }, 1800);
    });
  }
})();
