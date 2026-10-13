// A TCP proxy between the browser and the spike server that adds real latency and outages.
// Chromium's DevTools network emulation delays HTTP requests but not WebSocket frames, so it can't
// show what a round trip costs a circuit. This proxy delays every byte in both directions.
//   rtt:            round-trip time added, split evenly between the two directions
//   stall(ms):      a dead zone: nothing gets through for ms, then everything queued is delivered,
//                   as TCP retransmission does when a phone loses signal briefly
//   reset(ms):      a network switch: every open connection is reset, new ones are refused for ms
import net from 'node:net';

export function startProxy({ listenPort, targetPort, host = '127.0.0.1', rtt = 0 }) {
  let oneWay = rtt / 2;
  let stallUntil = 0;
  let refuseUntil = 0;
  const sockets = new Set();

  /** Forwards chunks from one socket to the other after the one-way delay, keeping their order. */
  const pipe = (from, to) => {
    let last = 0;
    from.on('data', (chunk) => {
      const now = Date.now();
      const due = Math.max(now + oneWay, last, stallUntil + oneWay);
      last = due;
      const send = () => { if (!to.destroyed) to.write(chunk); };
      if (due <= now) send(); else setTimeout(send, due - now);
    });
    from.on('end', () => setTimeout(() => to.end(), Math.max(0, last - Date.now())));
  };

  const server = net.createServer((client) => {
    if (Date.now() < refuseUntil) { client.destroy(); return; }
    const upstream = net.connect(targetPort, host);
    client.setNoDelay(true);
    upstream.setNoDelay(true);
    sockets.add(client);
    sockets.add(upstream);
    const close = () => {
      client.destroy(); upstream.destroy();
      sockets.delete(client); sockets.delete(upstream);
    };
    for (const s of [client, upstream]) { s.on('error', close); s.on('close', close); }
    pipe(client, upstream);
    pipe(upstream, client);
  });

  return new Promise((resolve) => server.listen(listenPort, host, () => resolve({
    url: `http://${host}:${listenPort}`,
    setRtt: (ms) => { oneWay = ms / 2; },
    stall: (ms) => { stallUntil = Date.now() + ms; },
    reset: (ms) => {
      refuseUntil = Date.now() + ms;
      for (const s of sockets) s.destroy();
      sockets.clear();
    },
    close: () => new Promise((r) => { for (const s of sockets) s.destroy(); server.close(r); }),
  })));
}
