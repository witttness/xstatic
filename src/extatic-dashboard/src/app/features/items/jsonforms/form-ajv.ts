import Ajv, { type Options } from 'ajv';
import Ajv2019 from 'ajv/dist/2019.js';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { createAjv, type JsonSchema } from '@jsonforms/core';

const options: Options = {
  allErrors: true,
  verbose: true,
  strict: false,
  addUsedSchema: false,
};

export function createFormAjv(schema: JsonSchema): Ajv {
  const dialect = schema.$schema;
  if (dialect === 'https://json-schema.org/draft/2020-12/schema' ||
      dialect === 'https://json-schema.org/draft/2020-12/schema#') {
    return addFormats(new Ajv2020(options));
  }
  if (dialect === 'https://json-schema.org/draft/2019-09/schema' ||
      dialect === 'https://json-schema.org/draft/2019-09/schema#') {
    return addFormats(new Ajv2019(options));
  }
  return createAjv(options);
}
