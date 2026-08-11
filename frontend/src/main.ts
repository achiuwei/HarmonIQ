import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Module } from './Module';
import { setApiBase } from './base';
import { themes, defaultBrand } from './styles/themes';
import tokensCss from './styles/tokens.css?inline';
import baseCss from './styles/base.css?inline';

class HarmonIQModuleElement extends HTMLElement {
  static observedAttributes = ['listing-id', 'brand', 'state', 'api-base'];
  private root?: Root;
  private themeStyle?: HTMLStyleElement;

  connectedCallback() {
    const shadow = this.shadowRoot ?? this.attachShadow({ mode: 'open' });
    if (!this.root) {
      const style = document.createElement('style');
      style.textContent = tokensCss + '\n' + baseCss;
      shadow.appendChild(style);
      this.themeStyle = document.createElement('style');
      shadow.appendChild(this.themeStyle);
      const mount = document.createElement('div');
      shadow.appendChild(mount);
      this.root = createRoot(mount);
    }
    this.sync();
  }

  attributeChangedCallback() {
    if (this.root) this.sync();
  }

  disconnectedCallback() {
    // Defer unmount so brand-switcher DOM moves don't tear down state.
    queueMicrotask(() => {
      if (!this.isConnected) { this.root?.unmount(); this.root = undefined; }
    });
  }

  private sync() {
    setApiBase(this.getAttribute('api-base'));
    const brand = this.getAttribute('brand') ?? defaultBrand;
    this.themeStyle!.textContent = themes[brand] ?? themes[defaultBrand];
    this.root!.render(
      React.createElement(Module, {
        listingId: this.getAttribute('listing-id') ?? '',
        brand,
        initialState: (this.getAttribute('state') as 'badge' | 'expanded') ?? 'badge',
      }),
    );
  }
}

if (!customElements.get('harmoniq-module')) {
  customElements.define('harmoniq-module', HarmonIQModuleElement);
}
