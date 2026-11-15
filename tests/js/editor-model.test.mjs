// Modèle de l'éditeur visuel : opérations sur le fichier de formulaire, sans DOM.
// Lancement : node --test tests/js/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as M from '../../src/CseForms.Api/wwwroot/assets/js/editor-model.mjs';

function sample() {
  return {
    slug: 'essai',
    title: 'Formulaire fictif',
    futureProperty: { kept: true },
    sections: [
      {
        id: 'perso',
        title: 'Identité',
        fields: [
          { id: 'contrat', type: 'choice', label: 'Type de contrat', options: ['CDI', 'CDD'] },
          { id: 'dateFin', type: 'date', label: 'Date de fin', visibleIf: { field: 'contrat', op: 'notEquals', value: 'CDI' }, rules: [{ op: 'after', field: 'dateDebut' }] },
          { id: 'email', type: 'email', label: 'Email' },
        ],
      },
      {
        id: 'famille',
        title: 'Famille',
        visibleIf: { all: [{ field: 'contrat', op: 'equals', value: 'CDI' }] },
        fields: [
          { id: 'dateDebut', type: 'date', label: 'Date de début' },
          {
            id: 'enfants', type: 'repeat', label: 'Enfants',
            fields: [
              { id: 'prenom', type: 'text', label: 'Prénom' },
              { id: 'garde', type: 'text', label: 'Mode de garde', visibleIf: { field: 'prenom', op: 'notEmpty' } },
            ],
          },
        ],
      },
    ],
    receipt: { emailField: 'email' },
  };
}

test('un identifiant se déduit du libellé, sans accent ni ponctuation', () => {
  assert.equal(M.idFromLabel('Date de naissance'), 'dateDeNaissance');
  assert.equal(M.idFromLabel("Nom de l'accompagnant"), 'nomDeLAccompagnant');
  assert.equal(M.idFromLabel('Été 2026 : séjour'), 'ete2026Sejour');
  assert.equal(M.idFromLabel('2 enfants'), 'champ2Enfants');
  assert.equal(M.idFromLabel(''), 'champ');
  assert.equal(M.idFromLabel('a'.repeat(80)).length, 64);
});

test('un nouvel identifiant est libre dans tout le formulaire', () => {
  const doc = sample();
  assert.equal(M.uniqueId(doc, 'email'), 'email2');
  assert.equal(M.uniqueId(doc, 'perso'), 'perso2');
  assert.equal(M.uniqueId(doc, 'prenom'), 'prenom2');
  assert.equal(M.uniqueId(doc, 'email', 'email'), 'email');
  assert.equal(M.uniqueId(doc, 'nouveau'), 'nouveau');
});

test('les champs créés ont des valeurs de départ propres à leur type', () => {
  const doc = sample();
  assert.deepEqual(M.createField(doc, 'choice').options, ['Option 1', 'Option 2']);
  const repeat = M.createField(doc, 'repeat');
  assert.equal(repeat.fields.length, 1);
  assert.notEqual(repeat.fields[0].id, repeat.id);
  assert.equal(M.createField(doc, 'info').label, undefined);
  assert.equal(M.createField(doc, 'hidden').prefill, 'url');
  assert.equal(M.createSection(doc).id, 'section3');
});

test('un champ se déplace dans sa section, vers une autre section et dans un groupe', () => {
  const doc = sample();
  const [contrat, dateFin, email] = doc.sections[0].fields;
  assert.ok(M.moveNode(doc, contrat, doc.sections[0].fields, 3));
  assert.deepEqual(doc.sections[0].fields.map((f) => f.id), ['dateFin', 'email', 'contrat']);

  assert.ok(M.moveNode(doc, email, doc.sections[1].fields, 1));
  assert.deepEqual(doc.sections[1].fields.map((f) => f.id), ['dateDebut', 'email', 'enfants']);

  const enfants = doc.sections[1].fields[2];
  assert.ok(M.moveNode(doc, dateFin, enfants.fields, 0));
  assert.equal(M.parentRepeat(doc, dateFin), enfants);
  assert.equal(M.sectionOf(doc, dateFin), doc.sections[1]);
});

test('un groupe répétable ne peut pas entrer dans un groupe répétable', () => {
  const doc = sample();
  const enfants = doc.sections[1].fields[1];
  const autre = M.createField(doc, 'repeat');
  doc.sections[0].fields.push(autre);
  assert.equal(M.moveNode(doc, autre, enfants.fields, 0), false);
  assert.equal(M.moveNode(doc, enfants, enfants.fields, 0), false);
  assert.equal(doc.sections[0].fields.at(-1), autre);
});

test('les flèches passent un champ d\'une section à la voisine, jamais hors de son groupe', () => {
  const doc = sample();
  const email = doc.sections[0].fields[2];
  assert.equal(M.shiftField(doc, email, 1), doc.sections[1].fields);
  assert.equal(doc.sections[1].fields[0], email);
  assert.equal(M.shiftField(doc, email, -1), doc.sections[0].fields);
  assert.equal(doc.sections[0].fields.at(-1), email);

  const prenom = doc.sections[1].fields[1].fields[0];
  assert.equal(M.shiftField(doc, prenom, -1), null);
  assert.ok(M.shiftNode(doc, doc.sections[1], -1));
  assert.equal(doc.sections[0].id, 'famille');
});

test('renommer un champ met à jour conditions, règles et accusé de réception', () => {
  const doc = sample();
  M.renameField(doc, doc.sections[0].fields[0], 'typeContrat');
  M.renameField(doc, doc.sections[0].fields[2], 'emailPerso');
  M.renameField(doc, doc.sections[1].fields[0], 'debut');
  assert.equal(doc.sections[0].fields[1].visibleIf.field, 'typeContrat');
  assert.equal(doc.sections[1].visibleIf.all[0].field, 'typeContrat');
  assert.equal(doc.sections[0].fields[1].rules[0].field, 'debut');
  assert.equal(doc.receipt.emailField, 'emailPerso');
  assert.equal(M.countReferences(doc, 'typeContrat'), 2);
  assert.equal(M.countReferences(doc, 'contrat'), 0);
});

test('renommer un champ ou une section met à jour les sauts', () => {
  const doc = {
    sections: [
      { id: 'a', fields: [{ id: 'x', type: 'choice', label: 'X', options: ['1', '2'] }],
        jumps: [{ if: { field: 'x', op: 'equals', value: '1' }, goTo: 'c' }, { if: { any: [{ field: 'x', op: 'empty' }] }, end: 'fin' }] },
      { id: 'b', fields: [{ id: 'y', type: 'text', label: 'Y' }] },
      { id: 'c', fields: [{ id: 'z', type: 'text', label: 'Z' }] },
    ],
    endings: [{ id: 'fin', title: 'Fin' }],
  };
  M.renameField(doc, doc.sections[0].fields[0], 'choix');
  M.renameSection(doc, doc.sections[2], 'derniere');
  assert.equal(doc.sections[0].jumps[0].if.field, 'choix');
  assert.equal(doc.sections[0].jumps[1].if.any[0].field, 'choix');
  assert.equal(doc.sections[0].jumps[0].goTo, 'derniere');
  assert.equal(doc.sections[2].id, 'derniere');
  assert.equal(M.countReferences(doc, 'choix'), 2);
});

test('dupliquer un groupe donne de nouveaux identifiants et garde ses conditions internes', () => {
  const doc = sample();
  const enfants = doc.sections[1].fields[1];
  const copy = M.duplicateField(doc, enfants);
  assert.equal(doc.sections[1].fields[2], copy);
  assert.equal(copy.id, 'enfants2');
  assert.deepEqual(copy.fields.map((f) => f.id), ['prenom2', 'garde2']);
  assert.equal(copy.fields[1].visibleIf.field, 'prenom2');
  assert.equal(enfants.fields[1].visibleIf.field, 'prenom');
});

test('changer de type retire les propriétés qui ne s\'appliquent plus et garde les inconnues', () => {
  const doc = sample();
  const contrat = doc.sections[0].fields[0];
  contrat.display = 'radio';
  contrat.custom = 1;
  M.changeType(doc, contrat, 'text');
  assert.deepEqual(Object.keys(contrat).sort(), ['custom', 'id', 'label', 'type']);

  const dateFin = doc.sections[0].fields[1];
  dateFin.rules.push({ op: 'sameAs', field: 'dateDebut' });
  M.changeType(doc, dateFin, 'text');
  assert.deepEqual(dateFin.rules, [{ op: 'sameAs', field: 'dateDebut' }]);
  assert.ok(dateFin.visibleIf);

  M.changeType(doc, contrat, 'multichoice');
  assert.deepEqual(contrat.options, ['Option 1', 'Option 2']);
  M.changeType(doc, contrat, 'info');
  assert.equal(contrat.options, undefined);
  assert.equal(contrat.text, 'Texte à afficher');
});

test('les conditions simples et combinées se lisent et s\'écrivent ; les autres restent en JSON', () => {
  assert.deepEqual(M.readCondition(undefined), { mode: 'none', items: [] });
  const leaf = { field: 'a', op: 'empty' };
  assert.deepEqual(M.readCondition(leaf), { mode: 'single', items: [leaf] });
  assert.equal(M.readCondition({ any: [leaf, leaf] }).mode, 'any');
  assert.equal(M.readCondition({ all: [{ any: [leaf] }] }).mode, 'advanced');

  assert.equal(M.writeCondition('none', [leaf]), undefined);
  assert.equal(M.writeCondition('single', [leaf]), leaf);
  assert.deepEqual(M.writeCondition('all', [leaf]), { all: [leaf] });
  assert.equal(M.writeCondition('any', []), undefined);
});

test('une comparaison s\'adapte à sa cible et à son opérateur', () => {
  assert.deepEqual(M.defaultComparison({ id: 'c', type: 'choice', options: ['CDI'] }), { field: 'c', op: 'equals', value: 'CDI' });
  assert.deepEqual(M.defaultComparison({ id: 'ok', type: 'consent' }), { field: 'ok', op: 'equals', value: true });
  assert.deepEqual(M.defaultComparison({ id: 'n', type: 'text' }), { field: 'n', op: 'notEmpty' });

  const c = { field: 'c', op: 'equals', value: 'CDI' };
  M.setOperator(c, 'in');
  assert.deepEqual(c.value, ['CDI']);
  M.setOperator(c, 'notEquals');
  assert.equal(c.value, 'CDI');
  M.setOperator(c, 'empty');
  assert.equal('value' in c, false);
});

test('une condition cite les champs de sa ligne puis ceux du formulaire, jamais lui-même', () => {
  const doc = sample();
  const garde = doc.sections[1].fields[1].fields[1];
  const ids = M.conditionTargets(doc, garde).map((f) => f.id);
  assert.deepEqual(ids, ['prenom', 'contrat', 'dateFin', 'email', 'dateDebut']);
  assert.equal(M.describeCondition(doc, doc.sections[0].fields[1].visibleIf), 'si Type de contrat est différent de « CDI »');
});

test('un chemin d\'erreur du contrôle désigne la section ou le champ concerné', () => {
  const doc = sample();
  assert.equal(M.resolvePath(doc, '$.slug'), 'form');
  assert.equal(M.resolvePath(doc, '$.receipt.emailField'), 'form');
  assert.equal(M.resolvePath(doc, '$.sections[1].visibleIf.all[0].field'), doc.sections[1]);
  assert.equal(M.resolvePath(doc, '$.sections[0].fields[1].rules[0].field'), doc.sections[0].fields[1]);
  assert.equal(M.resolvePath(doc, '$.sections[1].fields[1].fields[0].label'), doc.sections[1].fields[1].fields[0]);
  assert.equal(M.resolvePath(doc, '$.sections[9].fields[0]'), 'form');
  assert.equal(M.resolvePath(doc, 'code'), 'form');
});

test('les opérations conservent les propriétés inconnues du fichier', () => {
  const doc = sample();
  M.moveNode(doc, doc.sections[0].fields[0], doc.sections[1].fields, 0);
  M.removeNode(doc, doc.sections[0].fields[0]);
  assert.deepEqual(doc.futureProperty, { kept: true });
  assert.equal(JSON.parse(JSON.stringify(doc)).futureProperty.kept, true);
});

function jumpSample() {
  return {
    sections: [
      { id: 'a', title: 'Éligibilité', fields: [{ id: 'x', type: 'choice', label: 'Salarié', options: ['oui', 'non'] }, { id: 'note', type: 'info', text: 'Info' }] },
      { id: 'b', title: 'Inscription', fields: [{ id: 'y', type: 'text', label: 'Nom' }] },
      { id: 'c', title: 'Fin', fields: [{ id: 'z', type: 'text', label: 'Commentaire' }] },
    ],
  };
}

test('les destinations d\'un saut sont les sections situées plus loin puis les pages de fin', () => {
  const doc = jumpSample();
  const ending = M.createEnding(doc);
  assert.deepEqual(doc.endings, [{ id: 'fin1', title: 'Page de fin 1' }]);
  assert.deepEqual(M.jumpDestinations(doc, doc.sections[0]).map((d) => d.value), ['goTo:b', 'goTo:c', `end:${ending.id}`]);
  assert.deepEqual(M.jumpDestinations(doc, doc.sections[2]).map((d) => d.value), ['end:fin1']);
});

test('un saut ne peut citer que des champs de la section ou des sections précédentes', () => {
  const doc = jumpSample();
  assert.deepEqual(M.jumpConditionTargets(doc, doc.sections[0]).map((f) => f.id), ['x']);
  assert.deepEqual(M.jumpConditionTargets(doc, doc.sections[1]).map((f) => f.id), ['x', 'y']);
});

test('ajouter un saut le place avant un saut sans condition, qui reste le dernier', () => {
  const doc = jumpSample();
  const section = doc.sections[0];
  section.jumps = [{ goTo: 'c' }];
  const jump = M.addJump(doc, section);
  assert.deepEqual(section.jumps, [jump, { goTo: 'c' }]);
  assert.deepEqual(jump, { if: { field: 'x', op: 'equals', value: 'oui' }, goTo: 'b' });

  M.setJumpDestination(jump, 'end:fin1');
  assert.deepEqual(jump, { if: { field: 'x', op: 'equals', value: 'oui' }, end: 'fin1' });
  assert.equal(M.jumpDestination(jump), 'end:fin1');

  M.removeJump(section, jump);
  M.removeJump(section, section.jumps[0]);
  assert.equal('jumps' in section, false);
});

test('renommer ou supprimer une page de fin suit les sauts qui y mènent', () => {
  const doc = jumpSample();
  const ending = M.createEnding(doc);
  doc.sections[0].jumps = [{ if: { field: 'x', op: 'equals', value: 'non' }, end: ending.id }];
  M.renameEnding(doc, ending, 'nonEligible');
  assert.equal(doc.sections[0].jumps[0].end, 'nonEligible');
  assert.deepEqual(M.jumpSources(doc, 'end', 'nonEligible'), [doc.sections[0]]);
  assert.equal(M.describeJump(doc, doc.sections[0].jumps[0]), 'si Salarié est égal à « non » : fin « Page de fin 1 »');

  assert.ok(M.removeEnding(doc, ending));
  assert.equal('endings' in doc, false);
});

test('les identifiants des pages de fin ne reprennent pas ceux des sections et des champs', () => {
  const doc = jumpSample();
  doc.sections[0].id = 'fin1';
  assert.equal(M.createEnding(doc).id, 'fin12');
  assert.equal(M.uniqueId(doc, 'fin12'), 'fin122');
});

test('une erreur sur une page de fin désigne cette page', () => {
  const doc = jumpSample();
  const ending = M.createEnding(doc);
  assert.equal(M.resolvePath(doc, '$.endings[0].title'), ending);
  assert.equal(M.resolvePath(doc, '$.endings[3]'), 'form');
  assert.equal(M.resolvePath(doc, '$.sections[1].jumps[0].goTo'), doc.sections[1]);
  assert.equal(M.nodeTitle(ending), 'Page de fin 1');
});
