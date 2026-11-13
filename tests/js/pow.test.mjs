// Preuve de travail côté client : même calcul que le serveur (SHA-256 du sel suivi du nombre).
// Lancement : node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sha256Hex, solveChallenge, createProofSource } from '../../src/CseForms.Api/wwwroot/assets/js/pow.mjs';

const salt = '0a1b2c3d4e5f60718293a4b5?expires=1893456000';

test("l'empreinte est l'hexadécimal minuscule de SHA-256", async () => {
  assert.equal(await sha256Hex('abc'), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('le nombre caché est retrouvé, avec la signature intacte', async () => {
  const challenge = await sha256Hex(`${salt}1234`);
  const solution = await solveChallenge({ algorithm: 'SHA-256', challenge, maxNumber: 5000, salt, signature: 'sig' });
  assert.deepEqual(solution, { algorithm: 'SHA-256', challenge, number: 1234, salt, signature: 'sig' });
});

test('le dernier nombre de la plage est atteint', async () => {
  const challenge = await sha256Hex(`${salt}1000`);
  const solution = await solveChallenge({ algorithm: 'SHA-256', challenge, maxNumber: 1000, salt, signature: 's' });
  assert.equal(solution.number, 1000);
});

test('un défi sans solution ou dans un autre algorithme renvoie null', async () => {
  const challenge = await sha256Hex(`${salt}999`);
  assert.equal(await solveChallenge({ algorithm: 'SHA-256', challenge, maxNumber: 500, salt, signature: 's' }), null);
  assert.equal(await solveChallenge({ algorithm: 'SHA-512', challenge, maxNumber: 1000, salt, signature: 's' }), null);
});

test('chaque prise renvoie une preuve neuve et prépare la suivante', async () => {
  let calls = 0;
  const source = createProofSource(async () => {
    calls += 1;
    return { algorithm: 'SHA-256', challenge: await sha256Hex(`${salt}${calls}`), maxNumber: 100, salt, signature: `s${calls}` };
  });

  source.prepare();
  const first = await source.take();
  const second = await source.take();

  assert.equal(first.signature, 's1');
  assert.equal(second.signature, 's2');
  assert.equal(calls, 3);
});

test("une erreur de récupération du défi ressort à la prise, puis un nouvel essai est possible", async () => {
  let fail = true;
  const source = createProofSource(async () => {
    if (fail) throw new Error('réseau');
    return { algorithm: 'SHA-256', challenge: await sha256Hex(`${salt}7`), maxNumber: 10, salt, signature: 'ok' };
  });

  await assert.rejects(source.take(), /réseau/);
  fail = false;
  source.prepare();
  assert.equal((await source.take()).number, 7);
});
