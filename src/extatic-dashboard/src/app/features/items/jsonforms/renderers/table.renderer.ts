import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  Pipe,
  PipeTransform,
  inject,
} from '@angular/core';
import { JsonFormsArrayControl, JsonFormsModule } from '@jsonforms/angular';
import {
  arrayDefaultTranslations,
  ArrayControlProps,
  ArrayTranslations,
  ControlElement,
  createDefaultValue,
  defaultJsonFormsI18nState,
  deriveTypes,
  encode,
  getArrayTranslations,
  isObjectArrayControl,
  isPrimitiveArrayControl,
  JsonSchema,
  mapDispatchToArrayControlProps,
  or,
  OwnPropsOfRenderer,
  Paths,
  RankedTester,
  rankWith,
  setReadonly,
  UISchemaElement,
} from '@jsonforms/core';
import { startCase } from '../start-case';

interface ColumnDescription {
  property: string;
  header: string;
  props: OwnPropsOfRenderer;
}

const controlWithoutLabel = (scope: string): ControlElement => ({
  type: 'Control',
  scope: scope,
  label: false,
});

@Pipe({ name: 'getProps' })
export class GetPropsPipe implements PipeTransform {
  transform(index: number, props: OwnPropsOfRenderer) {
    const rowPath = Paths.compose(props.path ?? '', `${index}`);
    return {
      schema: props.schema,
      uischema: props.uischema,
      path: rowPath,
    };
  }
}

@Component({
  selector: 'app-table-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-2">
      <div class="flex items-center gap-3">
        @if (label) {
          <span class="text-xs font-medium text-text-muted uppercase tracking-wider">{{ label }}</span>
        }
        @if (error) {
          <span class="text-xs text-error" role="alert">{{ error }}</span>
        }
        <span class="flex-1"></span>
        <button
          type="button"
          (click)="add()"
          [disabled]="!isEnabled()"
          [attr.aria-label]="translations.addAriaLabel"
          class="text-xs text-accent hover:underline disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {{ translations.addTooltip }}
        </button>
      </div>
      @if (noData) {
        <p class="text-xs text-text-muted">{{ translations.noDataMessage }}</p>
      }
      @if (!noData) {
        <div class="overflow-x-auto">
          <table class="w-full border-collapse text-sm">
            <thead>
              <tr>
                @for (column of columns; track column.property) {
                  <th
                    class="border-b border-border px-2 py-1 text-left text-xs font-medium text-text-muted uppercase tracking-wider"
                  >
                    {{ column.header }}
                  </th>
                }
                @if (isEnabled()) {
                  <th class="border-b border-border px-2 py-1"></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of rows; track $index) {
                <tr>
                  @for (column of columns; track column.property) {
                    <td class="border-b border-border last:border-0 px-2 py-1 align-top">
                      <jsonforms-outlet
                        [renderProps]="row | getProps : column.props"
                      />
                    </td>
                  }
                  @if (isEnabled()) {
                    <td class="border-b border-border last:border-0 px-2 py-1 whitespace-nowrap">
                      <div class="flex gap-4">
                        @if (showSortButtons()) {
                          <button type="button" (click)="up($index)" [disabled]="$index === 0"
                            class="text-xs text-accent hover:underline disabled:opacity-50 disabled:cursor-not-allowed">
                            {{ translations.up }}
                          </button>
                          <button type="button" (click)="down($index)" [disabled]="$index === rows.length - 1"
                            class="text-xs text-accent hover:underline disabled:opacity-50 disabled:cursor-not-allowed">
                            {{ translations.down }}
                          </button>
                        }
                        <button type="button" (click)="remove($index)"
                          class="text-xs text-error hover:underline">
                          {{ translations.removeTooltip }}
                        </button>
                      </div>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule, GetPropsPipe],
})
export class TableRenderer
  extends JsonFormsArrayControl
  implements OnInit
{
  detailUiSchema?: UISchemaElement;
  columns: ColumnDescription[] = [];
  rows: number[] = [];
  noData = false;
  readonly columnsToIgnore = ['array', 'object'];
  addItem!: (path: string, value: unknown) => () => void;
  moveItemUp!: (path: string, index: number) => () => void;
  moveItemDown!: (path: string, index: number) => () => void;
  removeItems!: (path: string, toDelete: number[]) => () => void;
  translations: ArrayTranslations = {};
  private changeDetectorRef = inject(ChangeDetectorRef);

  showSortButtons(): boolean {
    return !!this.uischema?.options?.['showSortButtons'];
  }

  override mapAdditionalProps(
    props: ArrayControlProps & { translations: ArrayTranslations },
  ) {
    this.columns = this.generateCells(props.schema, props.path);
    this.rows = Array.isArray(props.data)
      ? Array.from({ length: props.data.length }, (_unused, i) => i)
      : [];
    this.noData = this.rows.length === 0;
    this.translations = props.translations;
    this.changeDetectorRef.markForCheck();
  }

  remove(index: number): void {
    this.removeItems(this.propsPath, [index])();
  }
  add(): void {
    this.addItem(
      this.propsPath,
      createDefaultValue(this.scopedSchema, this.rootSchema),
    )();
  }
  up(index: number): void {
    this.moveItemUp(this.propsPath, index)();
  }
  down(index: number): void {
    this.moveItemDown(this.propsPath, index)();
  }

  override ngOnInit() {
    super.ngOnInit();

    const { addItem, removeItems, moveUp, moveDown } =
      mapDispatchToArrayControlProps(
        this.jsonFormsService.updateCore.bind(this.jsonFormsService),
      );
    this.addItem = addItem;
    this.moveItemUp = moveUp!;
    this.moveItemDown = moveDown!;
    this.removeItems = removeItems!;
  }

  generateCells = (
    schema: JsonSchema,
    rowPath: string,
  ): ColumnDescription[] => {
    if (schema.type === 'object') {
      return this.getValidColumnProps(schema).map((prop) => {
        const encProp = encode(prop);
        const uischema = controlWithoutLabel(`#/properties/${encProp}`);
        if (!this.isEnabled()) {
          setReadonly(uischema);
        }
        return {
          property: prop,
          header: startCase(prop),
          props: {
            schema: schema,
            uischema,
            path: rowPath,
          },
        };
      });
    }
    // needed to correctly render input control for multi attributes
    return [
      {
        property: 'DUMMY',
        header: this.label ?? '',
        props: {
          schema: schema,
          uischema: controlWithoutLabel('#'),
          path: rowPath,
        },
      },
    ];
  };

  getValidColumnProps = (scopedSchema: JsonSchema) => {
    if (scopedSchema.type === 'object') {
      return Object.keys(scopedSchema.properties ?? {}).filter((prop) => {
        const child = scopedSchema.properties?.[prop];
        if (!child) {
          return false;
        }
        const types = deriveTypes(child);
        if (types.length > 1) {
          return false;
        }
        return this.columnsToIgnore.indexOf(types[0]) === -1;
      });
    }
    // primitives
    return [''];
  };
}

export const tableRendererTester: RankedTester = rankWith(
  3,
  or(isObjectArrayControl, isPrimitiveArrayControl),
);
