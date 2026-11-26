import test from 'node:test';
import assert from 'node:assert/strict';
import { parseFragment, shouldRegisterSw, demoView, demoOverlay, formatLogEntry } from '../../js/main.js';

test('fragment: join keys pass through, page keys are taken out', () => {
  const f = parseFragment('#h=192.168.1.2&p=47000&u=abcdefgh&debug=1&w=x&f=y&t=z');
  assert.equal(f.debug, true);
  assert.equal(f.demo, null);
  assert.equal(f.joinHash, '#h=192.168.1.2&p=47000&u=abcdefgh&w=x&f=y&t=z');
  assert.equal(parseFragment('').joinHash, '');
  assert.equal(parseFragment('#debug=0').debug, false);
});

test('fragment: demo and overlay', () => {
  assert.deepEqual(parseFragment('#demo=holding:look-at-tv').demo, { index: false, screen: 'holding', variant: 'look-at-tv' });
  assert.deepEqual(parseFragment('#demo=card').demo, { index: false, screen: 'card', variant: null });
  assert.equal(parseFragment('#demo').demo.index, true);
  assert.equal(parseFragment('#demo=').demo.index, true);
  assert.deepEqual(parseFragment('#demo=joined&overlay=popup:failed:reconnect').overlay, { id: 'popup', args: ['failed', 'reconnect'] });
});

test('service worker only on secure origins', () => {
  assert.equal(shouldRegisterSw({ protocol: 'https:', hostname: 'example.invalid' }), true);
  assert.equal(shouldRegisterSw({ protocol: 'http:', hostname: 'localhost' }), true);
  assert.equal(shouldRegisterSw({ protocol: 'http:', hostname: '127.0.0.1' }), true);
  assert.equal(shouldRegisterSw({ protocol: 'http:', hostname: '192.168.1.5' }), false);
  assert.equal(shouldRegisterSw({ protocol: 'file:', hostname: '' }), false);
  assert.equal(shouldRegisterSw(null), false);
});

test('demo view picks the variant or the first demo', () => {
  const demos = [{ variant: 'a', data: { x: 1 } }, { variant: 'b', data: { x: 2 } }];
  assert.deepEqual(demoView('s', 'b', demos), { screen: 's', variant: 'b', data: { x: 2 }, seq: 1 });
  assert.deepEqual(demoView('s', null, demos), { screen: 's', variant: 'a', data: { x: 1 }, seq: 1 });
  assert.deepEqual(demoView('s', 'zz', demos), { screen: 's', variant: 'a', data: { x: 1 }, seq: 1 });
  assert.deepEqual(demoView('s', 'v', undefined), { screen: 's', variant: 'v', data: {}, seq: 1 });
});

test('demo overlays', () => {
  assert.deepEqual(demoOverlay({ id: 'popup', args: ['gamefull'] }).popup, { reason: 'gamefull', reconnect: false });
  assert.deepEqual(demoOverlay({ id: 'popup', args: ['failed', 'reconnect'] }).popup, { reason: 'failed', reconnect: true });
  assert.deepEqual(demoOverlay({ id: 'pause', args: [] }).overlays, [{ id: 'pause', data: {} }]);
  assert.equal(demoOverlay({ id: 'leave', args: [] }).local, 'leave');
  assert.equal(demoOverlay({ id: 'bad-connection', args: [] }).lastRxAgo, 9000);
  assert.equal(demoOverlay(null).popup, null);
});

test('log entries format', () => {
  assert.equal(formatLogEntry('x'), 'x');
  assert.equal(formatLogEntry({ t: 0, level: 'info', msg: 'hi' }), '00:00:00.000 info hi');
  assert.equal(formatLogEntry({ level: 'warn', message: 'm', data: { a: 1 } }), 'warn m {"a":1}');
  assert.equal(formatLogEntry(null), '');
});
