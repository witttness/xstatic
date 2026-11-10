import { JsonFormsRendererRegistryEntry } from '@jsonforms/core';
import {
  EnumControlRenderer,
  OneOfEnumControlRenderer,
  enumControlTester,
  oneOfEnumControlTester,
} from './enum.renderer';
import {
  DateControlRenderer,
  DateTimeControlRenderer,
  TimeControlRenderer,
  dateControlTester,
  dateTimeControlTester,
  timeControlTester,
} from './date-time.renderer';
import {
  BooleanControlRenderer,
  booleanControlTester,
} from './boolean.renderer';
import {
  GroupLayoutRenderer,
  groupLayoutTester,
} from './group-layout.renderer';
import {
  HorizontalLayoutRenderer,
  horizontalLayoutTester,
} from './horizontal-layout.renderer';
import { LabelRenderer, labelRendererTester } from './label.renderer';
import {
  NumberControlRenderer,
  numberControlTester,
} from './number.renderer';
import {
  ObjectControlRenderer,
  objectControlTester,
} from './object.renderer';
import {
  TableRenderer,
  tableRendererTester,
} from './table.renderer';
import {
  TextAreaRenderer,
  textAreaTester,
} from './textarea.renderer';
import { TextControlRenderer, textControlTester } from './text.renderer';
import {
  VerticalLayoutRenderer,
  verticalLayoutTester,
} from './vertical-layout.renderer';
import {
  ArrayLayoutRenderer,
  arrayLayoutTester,
} from './array-layout.renderer';

/** Renderers styled with the dashboard's Tailwind theme tokens. */
export const tailwindRenderers: JsonFormsRendererRegistryEntry[] = [
  { tester: textControlTester, renderer: TextControlRenderer },
  { tester: textAreaTester, renderer: TextAreaRenderer },
  { tester: numberControlTester, renderer: NumberControlRenderer },
  { tester: booleanControlTester, renderer: BooleanControlRenderer },
  { tester: enumControlTester, renderer: EnumControlRenderer },
  { tester: oneOfEnumControlTester, renderer: OneOfEnumControlRenderer },
  { tester: dateControlTester, renderer: DateControlRenderer },
  { tester: timeControlTester, renderer: TimeControlRenderer },
  { tester: dateTimeControlTester, renderer: DateTimeControlRenderer },
  { tester: objectControlTester, renderer: ObjectControlRenderer },
  { tester: arrayLayoutTester, renderer: ArrayLayoutRenderer },
  { tester: tableRendererTester, renderer: TableRenderer },
  { tester: verticalLayoutTester, renderer: VerticalLayoutRenderer },
  { tester: horizontalLayoutTester, renderer: HorizontalLayoutRenderer },
  { tester: groupLayoutTester, renderer: GroupLayoutRenderer },
  { tester: labelRendererTester, renderer: LabelRenderer },
];

export * from './enum.renderer';
export * from './date-time.renderer';
export * from './boolean.renderer';
export * from './group-layout.renderer';
export * from './horizontal-layout.renderer';
export * from './label.renderer';
export * from './number.renderer';
export * from './object.renderer';
export * from './table.renderer';
export * from './textarea.renderer';
export * from './text.renderer';
export * from './vertical-layout.renderer';
export * from './array-layout.renderer';
export * from './layout.renderer';
