import {
  findUISchema,
  setReadonly,
  StatePropsOfControlWithDetail,
  UISchemaElement,
} from '@jsonforms/core';
import { depsChanged } from './deps';

const EMPTY_UISCHEMAS: never[] = [];

/**
 * Creates a memoized `findUISchema` for renderers rendering a detail.
 *
 * `findUISchema` can return the inline `options.detail` or a registered UI
 * schema, i.e. objects owned by the user, so the result is always a copy, on
 * which the `readonly` option is set when disabled. It is only recalculated
 * when one of its inputs changed, so it can be compared by identity.
 *
 * Adapted from EclipseSource JSON Forms (MIT), see
 * https://github.com/eclipsesource/jsonforms
 */
export const createDetailUiSchemaResolver = () => {
  let deps: unknown[] | undefined;
  let detailUiSchema: UISchemaElement | undefined;
  return (
    props: StatePropsOfControlWithDetail,
    enabled: boolean,
    schemaPath: string,
    fallback?: string | (() => UISchemaElement),
  ): UISchemaElement => {
    // `schemaPath` and `fallback` are derived from these as well
    const nextDeps = [
      props.uischema,
      props.uischemas,
      props.schema,
      props.rootSchema,
      props.path,
      enabled,
    ];
    if (!depsChanged(deps, nextDeps)) {
      return detailUiSchema as UISchemaElement;
    }
    deps = nextDeps;

    detailUiSchema = structuredClone(
      findUISchema(
        props.uischemas ?? EMPTY_UISCHEMAS,
        props.schema,
        schemaPath,
        props.path,
        fallback,
        props.uischema,
        props.rootSchema,
      ),
    ) as UISchemaElement;
    if (!enabled) {
      setReadonly(detailUiSchema);
    }
    return detailUiSchema;
  };
};
