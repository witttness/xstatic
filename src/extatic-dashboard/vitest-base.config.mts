import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    server: {
      deps: {
        // JSON Forms publishes extensionless lodash imports that Node cannot load directly.
        inline: ['@jsonforms/angular', '@jsonforms/core'],
      },
    },
  },
});
