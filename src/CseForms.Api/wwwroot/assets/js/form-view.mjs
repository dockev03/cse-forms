// Rendu du formulaire public à partir de sa définition JSON.
// Tout le contenu est inséré avec textContent : aucun HTML venant de la définition n'est interprété.

import { computeVisibility, todayInParis, validate } from './engine.mjs';

const RADIO_MAX_OPTIONS = 4;

/** Identifiant d'élément dérivé d'un chemin d'erreur : enfants[0].nom donne f-enfants-0-nom. */
export const elementId = (path) => `f-${path.replace(/\[(\d+)\]/g, '-$1').replace(/\./g, '-')}`;

function el(tag, attributes = {}, ...children) {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    if (value === undefined || value === null || value === false) continue;
    if (name === 'text') element.textContent = value;
    else if (name === 'class') element.className = value;
    else if (value === true) element.setAttribute(name, '');
    else element.setAttribute(name, String(value));
  }
  element.append(...children.filter(Boolean));
  return element;
}

function labelContent(field) {
  const parts = [document.createTextNode(field.label ?? field.id)];
  if (field.required) {
    parts.push(document.createTextNode(' '), el('span', { class: 'required-mark', 'aria-hidden': 'true', text: '*' }));
  }
  return parts;
}

export class FormView {
  /**
   * @param {HTMLElement} root
   * @param {{slug: string, title: string, version: number, requiresAccessCode: boolean,
   *          definition: {sections: object[]}, ui: object | null, sites: string[]}} form
   * @param {(payload: object) => Promise<object>} submit envoie la réponse et renvoie le résultat de l'API
   */
  constructor(root, form, submit) {
    this.root = root;
    this.form = form;
    this.definition = form.definition;
    this.ui = form.ui ?? {};
    this.submitHandler = submit;
    this.values = {};
    this.registry = new Map(); // chemin -> { wrapper, error, controls }
    this.sectionElements = new Map();
    this.topFields = new Map();
    this.repeats = new Map(); // id -> { field, rowsContainer, addButton }
    this.submitted = false;
    this.step = 0;
    this.multiStep = this.ui.multiStep === true;
  }

  render() {
    this.applyUrlPrefill();

    this.summary = el('div', { class: 'summary', role: 'alert', tabindex: '-1', hidden: true });
    this.formElement = el('form', { novalidate: true, 'aria-labelledby': 'form-title' });
    this.formElement.addEventListener('submit', (event) => {
      event.preventDefault();
      this.handleSubmit();
    });

    if (this.form.requiresAccessCode) {
      this.formElement.append(this.renderAccessCode());
    }

    for (const section of this.definition.sections) {
      const element = this.renderSection(section);
      this.sectionElements.set(section, element);
      this.formElement.append(element);
    }

    this.progress = el('div', { class: 'progress', hidden: !this.multiStep });
    this.previousButton = el('button', { type: 'button', text: 'Précédent' });
    this.nextButton = el('button', { type: 'button', class: 'primary', text: 'Suivant' });
    this.submitButton = el('button', { type: 'submit', class: 'primary', text: 'Envoyer' });
    this.previousButton.addEventListener('click', () => this.goToStep(this.step - 1));
    this.nextButton.addEventListener('click', () => this.handleNext());
    this.formElement.append(el('div', { class: 'actions' }, this.previousButton, this.nextButton, this.submitButton));
    this.formElement.prepend(this.progress);

    this.root.replaceChildren(
      el('h1', { id: 'form-title', text: this.form.title }),
      el('p', { class: 'note', text: 'Les champs marqués d\'un astérisque (*) sont obligatoires.' }),
      this.summary,
      this.formElement,
    );

    this.refresh();
  }

  // -------------------------------------------------------------------------
  // Valeurs

  /** Préremplissage par paramètre d'URL, uniquement pour les valeurs de la liste blanche. */
  applyUrlPrefill() {
    const params = new URLSearchParams(window.location.search);
    for (const section of this.definition.sections) {
      for (const field of section.fields) {
        if (field.prefill !== 'url' || !params.has(field.id)) continue;
        const value = params.get(field.id);
        const allowed = field.type === 'site' ? this.form.sites : field.options ?? [];
        if (allowed.includes(value)) this.values[field.id] = value;
      }
    }
  }

  changed() {
    this.refresh();
  }

  // -------------------------------------------------------------------------
  // Rendu

  renderAccessCode() {
    const id = 'f-accessCode';
    this.accessCodeInput = el('input', {
      id, type: 'text', inputmode: 'numeric', autocomplete: 'off', required: true,
      'aria-required': 'true', 'aria-describedby': `${id}-hint ${id}-error`, maxlength: 64,
    });
    const error = el('p', { id: `${id}-error`, class: 'error-message', hidden: true });
    const wrapper = el('div', { class: 'field card' },
      el('label', { for: id }, ...labelContent({ label: 'Code d\'accès', required: true })),
      el('p', { id: `${id}-hint`, class: 'hint', text: 'Il figure sur le document qui vous a été remis.' }),
      this.accessCodeInput,
      error);
    this.registry.set('accessCode', { wrapper, error, controls: [this.accessCodeInput] });
    return wrapper;
  }

  renderSection(section) {
    const content = [];
    if (section.description) content.push(el('p', { class: 'hint', text: section.description }));
    for (const field of section.fields) {
      const wrapper = this.renderField(field, field.id, () => this.values[field.id], (v) => this.setTop(field.id, v));
      if (wrapper) {
        this.topFields.set(field.id, wrapper);
        content.push(wrapper);
      }
    }

    if (section.collapsed) {
      return el('details', { class: 'section' }, el('summary', { text: section.title ?? '' }), ...content);
    }
    return el('section', { class: 'section', 'aria-label': section.title ?? undefined },
      section.title ? el('h2', { text: section.title }) : null, ...content);
  }

  setTop(id, value) {
    if (value === undefined) delete this.values[id];
    else this.values[id] = value;
    this.changed();
  }

  /** Crée le bloc d'un champ et l'enregistre pour l'affichage des erreurs. */
  renderField(field, path, get, set) {
    const id = elementId(path);
    const hintId = field.help ? `${id}-hint` : null;
    const errorId = `${id}-error`;
    const describedBy = [hintId, errorId].filter(Boolean).join(' ');
    const hint = field.help ? el('p', { id: hintId, class: 'hint', text: field.help }) : null;
    const error = el('p', { id: errorId, class: 'error-message', hidden: true });

    let wrapper;
    let controls = [];

    switch (field.type) {
      case 'hidden':
        return null;

      case 'info':
        return el('div', { class: 'field' },
          field.label ? el('p', { text: field.label }) : null,
          field.text ? el('p', { class: 'info', text: field.text }) : null);

      case 'consent': {
        const input = el('input', {
          id, type: 'checkbox', 'aria-describedby': describedBy, 'aria-required': field.required ? 'true' : undefined,
        });
        input.checked = get() === true;
        input.addEventListener('change', () => set(input.checked));
        controls = [input];
        wrapper = el('div', { class: 'field' },
          el('label', { class: 'choice', for: id }, input, el('span', {}, ...labelContent(field))), hint, error);
        break;
      }

      case 'choice': {
        const useRadio = field.display === 'radio' || (field.display !== 'select' && field.options.length <= RADIO_MAX_OPTIONS);
        if (useRadio) {
          controls = field.options.map((option, i) => {
            const input = el('input', { id: `${id}-${i}`, type: 'radio', name: id, value: option });
            input.checked = get() === option;
            input.addEventListener('change', () => input.checked && set(option));
            return input;
          });
          wrapper = el('fieldset', { class: 'field', id, 'aria-describedby': describedBy, tabindex: '-1' },
            el('legend', {}, ...labelContent(field)), hint,
            ...controls.map((input, i) => el('label', { class: 'choice', for: input.id }, input, el('span', { text: field.options[i] }))),
            error);
          break;
        }
        const select = this.renderSelect(id, field.options, get, set, describedBy, field.required);
        controls = [select];
        wrapper = el('div', { class: 'field' }, el('label', { for: id }, ...labelContent(field)), hint, select, error);
        break;
      }

      case 'site': {
        const select = this.renderSelect(id, this.form.sites, get, set, describedBy, field.required);
        controls = [select];
        wrapper = el('div', { class: 'field' }, el('label', { for: id }, ...labelContent(field)), hint, select, error);
        break;
      }

      case 'multichoice': {
        controls = field.options.map((option, i) => {
          const input = el('input', { id: `${id}-${i}`, type: 'checkbox', value: option });
          input.checked = Array.isArray(get()) && get().includes(option);
          input.addEventListener('change', () => {
            const checked = controls.filter((c) => c.checked).map((c) => c.value);
            set(checked.length ? checked : undefined);
          });
          return input;
        });
        wrapper = el('fieldset', { class: 'field', id, 'aria-describedby': describedBy, tabindex: '-1' },
          el('legend', {}, ...labelContent(field)), hint,
          ...controls.map((input, i) => el('label', { class: 'choice', for: input.id }, input, el('span', { text: field.options[i] }))),
          error);
        break;
      }

      case 'address':
        return this.renderAddress(field, path, get, set, hint, error, describedBy);

      case 'repeat':
        return this.renderRepeat(field, path, hint, error, describedBy);

      default: {
        const control = this.renderTextControl(field, id, get, set, describedBy);
        controls = [control];
        wrapper = el('div', { class: 'field' }, el('label', { for: id }, ...labelContent(field)), hint, control, error);
      }
    }

    this.registry.set(path, { wrapper, error, controls });
    return wrapper;
  }

  renderTextControl(field, id, get, set, describedBy) {
    const common = {
      id,
      'aria-describedby': describedBy,
      'aria-required': field.required ? 'true' : undefined,
      placeholder: field.placeholder,
      maxlength: field.maxLength,
    };
    const types = {
      email: { type: 'email', inputmode: 'email', autocomplete: 'email', spellcheck: 'false' },
      phone: { type: 'tel', inputmode: 'tel', autocomplete: 'tel' },
      date: { type: 'date' },
    };

    const control = field.type === 'textarea'
      ? el('textarea', common)
      : el('input', { type: 'text', ...common, ...(types[field.type] ?? {}) });
    control.value = typeof get() === 'string' ? get() : '';
    control.addEventListener('input', () => set(control.value === '' ? undefined : control.value));
    return control;
  }

  renderSelect(id, options, get, set, describedBy, required) {
    const select = el('select', { id, 'aria-describedby': describedBy, 'aria-required': required ? 'true' : undefined },
      el('option', { value: '', text: 'Choisir…' }),
      ...options.map((option) => el('option', { value: option, text: option })));
    select.value = options.includes(get()) ? get() : '';
    select.addEventListener('change', () => set(select.value === '' ? undefined : select.value));
    return select;
  }

  renderAddress(field, path, get, set, hint, error, describedBy) {
    const id = elementId(path);
    const parts = [
      { name: 'street', label: 'Numéro et rue', autocomplete: 'street-address' },
      { name: 'postalCode', label: 'Code postal', autocomplete: 'postal-code', inputmode: 'numeric', maxlength: 5 },
      { name: 'city', label: 'Ville', autocomplete: 'address-level2' },
    ];

    const blocks = parts.map((part) => {
      const partPath = `${path}.${part.name}`;
      const partId = elementId(partPath);
      const partError = el('p', { id: `${partId}-error`, class: 'error-message', hidden: true });
      const input = el('input', {
        id: partId, type: 'text', autocomplete: part.autocomplete, inputmode: part.inputmode, maxlength: part.maxlength,
        'aria-describedby': `${partId}-error`, 'aria-required': field.required ? 'true' : undefined,
      });
      input.value = get()?.[part.name] ?? '';
      input.addEventListener('input', () => {
        const next = { ...(get() ?? {}), [part.name]: input.value };
        set(Object.values(next).some((v) => v !== '') ? next : undefined);
      });
      const block = el('div', { class: 'address-part' }, el('label', { for: partId, text: part.label }), input, partError);
      this.registry.set(partPath, { wrapper: block, error: partError, controls: [input] });
      return block;
    });

    const wrapper = el('fieldset', { class: 'field', id, 'aria-describedby': describedBy, tabindex: '-1' },
      el('legend', {}, ...labelContent(field)), hint, ...blocks, error);
    this.registry.set(path, { wrapper, error, controls: [] });
    return wrapper;
  }

  renderRepeat(field, path, hint, error, describedBy) {
    const id = elementId(path);
    const rowsContainer = el('div', { class: 'repeat-rows' });
    const addButton = el('button', { type: 'button', text: field.addLabel ?? 'Ajouter' });
    addButton.addEventListener('click', () => {
      const rows = this.rows(field);
      rows.push({});
      this.values[field.id] = rows;
      this.renderRows(field);
      this.changed();
      rowsContainer.lastElementChild?.querySelector('input, select, textarea')?.focus();
    });

    const wrapper = el('fieldset', { class: 'field', id, 'aria-describedby': describedBy, tabindex: '-1' },
      el('legend', {}, ...labelContent(field)), hint, rowsContainer, addButton, error);
    this.registry.set(path, { wrapper, error, controls: [] });
    this.repeats.set(field.id, { field, rowsContainer, addButton, rowWrappers: [] });
    this.renderRows(field);
    return wrapper;
  }

  rows(field) {
    return Array.isArray(this.values[field.id]) ? this.values[field.id] : [];
  }

  /** Reconstruit les lignes d'un groupe : les indices des chemins restent alignés sur les valeurs. */
  renderRows(field) {
    const repeat = this.repeats.get(field.id);
    for (const key of [...this.registry.keys()]) {
      if (key.startsWith(`${field.id}[`)) this.registry.delete(key);
    }

    repeat.rowWrappers = this.rows(field).map((row, index) => {
      const rowPath = `${field.id}[${index}]`;
      const subWrappers = new Map();
      const content = field.fields.map((sub) => {
        const wrapper = this.renderField(
          sub,
          `${rowPath}.${sub.id}`,
          () => this.rows(field)[index]?.[sub.id],
          (value) => {
            const rows = this.rows(field);
            if (value === undefined) delete rows[index][sub.id];
            else rows[index][sub.id] = value;
            this.changed();
          });
        if (wrapper) subWrappers.set(sub.id, wrapper);
        return wrapper;
      });

      const remove = el('button', { type: 'button', class: 'link', text: 'Retirer cette ligne' });
      remove.addEventListener('click', () => {
        const rows = this.rows(field);
        rows.splice(index, 1);
        if (rows.length === 0) delete this.values[field.id];
        this.renderRows(field);
        this.changed();
        repeat.addButton.focus();
      });

      const rowElement = el('fieldset', { class: 'repeat-row', id: elementId(rowPath), tabindex: '-1' },
        el('legend', { text: `${field.label ?? field.id} n° ${index + 1}` }), ...content, remove);
      this.registry.set(rowPath, { wrapper: rowElement, error: null, controls: [] });
      return { rowElement, subWrappers };
    });

    repeat.rowsContainer.replaceChildren(...repeat.rowWrappers.map((r) => r.rowElement));
  }

  // -------------------------------------------------------------------------
  // Visibilité et étapes

  visibleSections() {
    const visibility = computeVisibility(this.definition, this.values);
    return this.definition.sections.filter((s) => visibility.section(s));
  }

  refresh() {
    const visibility = computeVisibility(this.definition, this.values);

    for (const [section, element] of this.sectionElements) {
      element.hidden = !visibility.section(section);
    }
    for (const [id, wrapper] of this.topFields) {
      wrapper.hidden = !visibility.field(id);
    }
    for (const { field, rowWrappers, addButton } of this.repeats.values()) {
      rowWrappers.forEach(({ subWrappers }, index) => {
        for (const [subId, wrapper] of subWrappers) {
          wrapper.hidden = !visibility.rowField(field, index, subId);
        }
      });
      addButton.hidden = this.rows(field).length >= (field.max ?? 100);
    }

    this.updateSteps();

    if (this.submitted) this.showErrors(this.validate().errors, { focus: false });
  }

  updateSteps() {
    const steps = this.visibleSections();
    if (!this.multiStep) {
      this.previousButton.hidden = true;
      this.nextButton.hidden = true;
      this.submitButton.hidden = false;
      return;
    }

    this.step = Math.min(this.step, steps.length - 1);
    for (const [section, element] of this.sectionElements) {
      if (!element.hidden) element.hidden = section !== steps[this.step];
    }
    const last = this.step === steps.length - 1;
    this.previousButton.hidden = this.step === 0;
    this.nextButton.hidden = last;
    this.submitButton.hidden = !last;

    const label = `Étape ${this.step + 1} sur ${steps.length}`;
    this.progress.replaceChildren(
      el('p', { id: 'progress-label', text: label }),
      el('progress', { max: steps.length, value: this.step + 1, 'aria-labelledby': 'progress-label' }));
  }

  goToStep(step) {
    this.step = Math.max(0, step);
    this.refresh();
    this.root.querySelector('h1')?.scrollIntoView();
    this.progress.querySelector('p')?.focus?.();
  }

  sectionOfPath(path) {
    const id = path.split(/[.[]/)[0];
    return this.definition.sections.find((s) => s.fields.some((f) => f.id === id));
  }

  handleNext() {
    const current = this.visibleSections()[this.step];
    const errors = this.validate().errors.filter((e) => this.sectionOfPath(e.path) === current);
    if (errors.length > 0) {
      this.showErrors(errors, { focus: true });
      return;
    }
    this.clearErrors();
    this.goToStep(this.step + 1);
  }

  // -------------------------------------------------------------------------
  // Validation et envoi

  validate() {
    const result = validate(this.definition, this.values, { today: todayInParis(), sites: this.form.sites });
    if (this.form.requiresAccessCode && this.accessCodeInput.value.trim() === '') {
      result.errors.unshift({ path: 'accessCode', code: 'required', message: '« Code d\'accès » est obligatoire.' });
    }
    return result;
  }

  clearErrors() {
    for (const { wrapper, error, controls } of this.registry.values()) {
      wrapper.classList.remove('has-error');
      if (error) {
        error.hidden = true;
        error.textContent = '';
      }
      for (const control of controls) control.removeAttribute('aria-invalid');
    }
    this.summary.hidden = true;
    this.summary.replaceChildren();
  }

  /** Remonte le chemin jusqu'à un élément affiché : enfants[0].nom, puis enfants[0], puis enfants. */
  entryFor(path) {
    let current = path;
    for (;;) {
      if (this.registry.has(current)) return [current, this.registry.get(current)];
      const parent = current.replace(/(\.[^.[\]]+|\[\d+\])$/, '');
      if (parent === current || parent === '') return [null, null];
      current = parent;
    }
  }

  showErrors(errors, { focus, title = 'Certaines réponses sont à corriger' }) {
    this.clearErrors();
    if (errors.length === 0) return;

    const links = [];
    for (const error of errors) {
      const [key, entry] = this.entryFor(error.path);
      if (entry?.error) {
        entry.error.textContent = entry.error.textContent ? `${entry.error.textContent} ${error.message}` : error.message;
        entry.error.hidden = false;
      }
      entry?.wrapper.classList.add('has-error');
      for (const control of entry?.controls ?? []) control.setAttribute('aria-invalid', 'true');

      const target = key ? (entry.controls[0]?.id ?? entry.wrapper.id) : null;
      const link = el('a', { href: target ? `#${target}` : undefined, text: error.message });
      link.addEventListener('click', (event) => {
        event.preventDefault();
        if (target) document.getElementById(target)?.focus();
      });
      links.push(el('li', {}, link));
    }

    this.summary.replaceChildren(el('h2', { text: title }), el('ul', {}, ...links));
    this.summary.hidden = false;
    if (focus) this.summary.focus();
  }

  showGlobalError(text, action) {
    this.clearErrors();
    this.summary.replaceChildren(el('h2', { text: 'Envoi impossible' }), el('p', { text }), action ?? null);
    this.summary.hidden = false;
    this.summary.focus();
  }

  async handleSubmit() {
    this.submitted = true;
    const { errors } = this.validate();
    if (errors.length > 0) {
      if (this.multiStep) {
        const steps = this.visibleSections();
        const index = steps.indexOf(this.sectionOfPath(errors[0].path));
        if (index >= 0 && index !== this.step) {
          this.step = index;
          this.updateSteps();
        }
      }
      this.showErrors(errors, { focus: true });
      return;
    }

    this.submitButton.disabled = true;
    this.formElement.setAttribute('aria-busy', 'true');
    try {
      const result = await this.submitHandler({
        version: this.form.version,
        accessCode: this.form.requiresAccessCode ? this.accessCodeInput.value.trim() : undefined,
        data: this.values,
      });
      this.handleResult(result);
    } catch {
      this.showGlobalError('Le serveur ne répond pas. Vérifiez votre connexion puis réessayez.');
    } finally {
      this.submitButton.disabled = false;
      this.formElement.removeAttribute('aria-busy');
    }
  }

  handleResult({ status, body }) {
    if (status === 201 && body?.caseNumber) {
      // Le serveur fait foi pour la page de fin ; absente, c'est la confirmation standard.
      this.showConfirmation(body.caseNumber, this.endingById(body.ending));
      return;
    }

    switch (body?.code) {
      case 'preview':
        this.showPreviewDone();
        return;
      case 'invalid':
        this.showErrors(body.errors ?? [], { focus: true });
        return;
      case 'accessDenied':
        this.showErrors([{ path: 'accessCode', code: 'accessDenied', message: body.message }], { focus: true });
        return;
      case 'versionChanged': {
        const reload = el('button', { type: 'button', class: 'primary', text: 'Recharger le formulaire' });
        reload.addEventListener('click', () => window.location.reload());
        this.showGlobalError(body.message, reload);
        return;
      }
      default:
        this.showGlobalError(body?.message ?? 'Une erreur est survenue. Réessayez plus tard.');
    }
  }

  /** Page de fin déclarée dans la définition, ou null (confirmation standard). */
  endingById(id) {
    if (typeof id !== 'string') return null;
    return (this.definition.endings ?? []).find((e) => e.id === id) ?? null;
  }

  /** Texte d'une page de fin, ou texte de confirmation standard (ui.confirmationText). */
  endingContent(ending) {
    if (ending) return ending.text ? el('p', { class: 'info', text: ending.text }) : null;
    return this.ui.confirmationText ? el('p', { class: 'info', text: this.ui.confirmationText }) : null;
  }

  /** Aperçu du brouillon : la saisie est valide, mais rien n'a été envoyé. */
  showPreviewDone() {
    const ending = this.endingById(computeVisibility(this.definition, this.values).ending);
    const heading = el('h1', { tabindex: '-1', text: 'Aperçu : la saisie est valide' });
    const restart = el('button', { type: 'button', class: 'primary', text: 'Recommencer la saisie' });
    restart.addEventListener('click', () => window.location.reload());
    this.root.replaceChildren(
      heading,
      el('div', { class: 'card confirmation', role: 'status' },
        el('p', { text: 'Rien n\'a été envoyé ni enregistré. Sur le formulaire publié, le répondant verrait ici son numéro de dossier.' }),
        el('p', { text: ending ? `Page de fin : « ${ending.title} »` : 'Page de fin : confirmation standard' }),
        this.endingContent(ending)),
      el('div', { class: 'actions' }, restart));
    heading.focus();
  }

  showConfirmation(caseNumber, ending = null) {
    const heading = el('h1', { tabindex: '-1', text: ending?.title ?? 'Merci, votre réponse est enregistrée' });
    this.root.replaceChildren(
      heading,
      el('div', { class: 'card confirmation', role: 'status' },
        el('p', { text: 'Votre numéro de dossier :' }),
        el('p', { class: 'case-number', text: caseNumber }),
        el('p', { text: 'Conservez ce numéro : il vous sera demandé pour toute question sur votre dossier.' }),
        this.endingContent(ending)));
    heading.focus();
  }
}
