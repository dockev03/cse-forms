// Exécute les jeux de tests partagés avec le moteur client.
// Lancement : node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  validate,
  computeVisibility,
  resolveDateExpression,
  todayInParis,
  isValidDate,
} from '../../src/CseForms.Api/wwwroot/assets/js/engine.mjs';

const fixtureDirectory = new URL('../fixtures/form-engine/', import.meta.url);
const files = readdirSync(fixtureDirectory).filter((f) => f.endsWith('.json')).sort();

test('les jeux partagés sont présents', () => assert.ok(files.length > 0));

for (const file of files) {
  const fixture = JSON.parse(readFileSync(new URL(file, fixtureDirectory), 'utf8'));
  for (const testCase of fixture.cases) {
    test(`${file} : ${testCase.name}`, () => {
      const result = validate(fixture.definition, testCase.input, { today: fixture.today, sites: fixture.sites });

      const expected = testCase.errors.map((e) => `${e.path} ${e.code}`).sort();
      const actual = result.errors.map((e) => `${e.path} ${e.code}`).sort();
      assert.deepEqual(actual, expected);

      if (testCase.data) {
        assert.deepEqual(result.data, testCase.data);
      }

      // « ending » : page de fin attendue (null pour la confirmation standard) ; non vérifiée si absente.
      if ('ending' in testCase) {
        assert.equal(result.ending, testCase.ending);
      }
    });
  }
}

test('une saisie qui n\'est pas un objet est refusée', () => {
  for (const input of [null, [], 'texte', 42]) {
    const result = validate({ sections: [] }, input, { today: '2026-10-30', sites: [] });
    assert.equal(result.data, null);
    assert.equal(result.errors[0].code, 'invalidType');
  }
});

test('les messages ne reprennent jamais la valeur saisie', () => {
  const definition = { sections: [{ id: 's', fields: [{ id: 'email', type: 'email', label: 'Email' }] }] };
  const result = validate(definition, { email: 'secret-valeur' }, { today: '2026-10-30', sites: [] });
  assert.equal(result.errors.length, 1);
  assert.ok(!result.errors[0].message.includes('secret-valeur'));
});

test('les dates relatives suivent les règles du serveur (fin de mois)', () => {
  assert.equal(resolveDateExpression('today+6M', '2026-08-31'), '2027-02-28');
  assert.equal(resolveDateExpression('today-18Y', '2028-02-29'), '2010-02-28');
  assert.equal(resolveDateExpression('today+30D', '2026-12-15'), '2027-01-14');
  assert.equal(resolveDateExpression('today', '2026-10-30'), '2026-10-30');
  assert.equal(resolveDateExpression('2027-12-31', '2026-10-30'), '2027-12-31');
  assert.equal(resolveDateExpression('demain', '2026-10-30'), null);
});

test('les dates impossibles sont refusées', () => {
  assert.ok(isValidDate('2028-02-29'));
  assert.ok(!isValidDate('2026-02-29'));
  assert.ok(!isValidDate('2026-13-01'));
  assert.ok(!isValidDate('30/10/2026'));
});

test('la date du jour est calculée dans le fuseau de Paris', () => {
  assert.equal(todayInParis(new Date('2026-12-31T23:30:00Z')), '2027-01-01');
  assert.equal(todayInParis(new Date('2026-12-31T22:59:59Z')), '2026-12-31');
  assert.equal(todayInParis(new Date('2026-06-30T22:30:00Z')), '2026-07-01');
});

test('la visibilité suit les conditions, y compris dans les lignes', () => {
  const fixture = JSON.parse(readFileSync(join(fixtureDirectory.pathname, 'inscription-fictive.json'), 'utf8'));
  const definition = fixture.definition;
  const enfants = definition.sections.find((s) => s.id === 'enfants').fields[0];

  const cdi = computeVisibility(definition, { contrat: 'CDI', enfants: [{}] });
  assert.equal(cdi.field('dateFin'), false);
  assert.equal(cdi.field('nom'), true);
  assert.equal(cdi.rowField(enfants, 0, 'naissance'), true);

  assert.equal(computeVisibility(definition, { contrat: 'CDD' }).field('dateFin'), true);
  assert.equal(computeVisibility(definition, null).field('dateFin'), true);
});

test('le rendu suit le parcours : sections sautées masquées et page de fin atteinte', () => {
  const fixture = JSON.parse(readFileSync(new URL('sauts-et-pages-de-fin.json', fixtureDirectory), 'utf8'));
  const { definition } = fixture;
  const shown = (input) => {
    const visibility = computeVisibility(definition, input);
    return { sections: definition.sections.filter((s) => visibility.section(s)).map((s) => s.id), ending: visibility.ending };
  };

  assert.deepEqual(shown({}), { sections: ['eligibilite', 'inscription', 'journee', 'fin'], ending: null });
  assert.deepEqual(shown({ salarie: 'non' }), { sections: ['eligibilite'], ending: 'nonEligible' });
  assert.deepEqual(shown({ salarie: 'oui', formule: 'soiree' }), { sections: ['eligibilite', 'inscription', 'soiree', 'fin'], ending: null });
  assert.deepEqual(
    shown({ salarie: 'oui', formule: 'soiree', accompagnant: 'oui' }),
    { sections: ['eligibilite', 'inscription', 'soiree', 'accompagnement'], ending: 'attente' });
});
