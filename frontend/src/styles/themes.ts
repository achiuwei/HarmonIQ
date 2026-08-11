export const themes: Record<string, string> = {
  apartments: `:host {
    --hiq-primary: #5f8f22; --hiq-primary-contrast: #fff; --hiq-accent: #2b4f12;
    --hiq-radius: 10px;
    --hiq-font-display: 'Helvetica Neue', Arial, sans-serif;
    --hiq-font-body: 'Helvetica Neue', Arial, sans-serif;
  }`,
  apartmentfinder: `:host {
    --hiq-primary: #0b6bb1; --hiq-primary-contrast: #fff; --hiq-accent: #f28c00;
    --hiq-radius: 6px;
    --hiq-font-display: Verdana, Geneva, sans-serif;
    --hiq-font-body: Verdana, Geneva, sans-serif;
  }`,
  forrent: `:host {
    --hiq-primary: #8a1e7c; --hiq-primary-contrast: #fff; --hiq-accent: #e84393;
    --hiq-radius: 18px;
    --hiq-font-display: Georgia, serif;
    --hiq-font-body: 'Trebuchet MS', Tahoma, sans-serif;
  }`,
};
export const defaultBrand = 'apartments';
