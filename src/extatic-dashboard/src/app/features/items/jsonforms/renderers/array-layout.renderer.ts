import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { JsonFormsAbstractControl, JsonFormsModule } from '@jsonforms/angular';
import {
  arrayDefaultTranslations,
  ArrayLayoutProps,
  ArrayTranslations,
  createDefaultValue,
  defaultJsonFormsI18nState,
  getArrayTranslations,
  isObjectArrayWithNesting,
  JsonFormsState,
  mapDispatchToArrayControlProps,
  mapStateToArrayLayoutProps,
  OwnPropsOfRenderer,
  Paths,
  RankedTester,
  rankWith,
  StatePropsOfArrayLayout,
  UISchemaElement,
} from '@jsonforms/core';
import { depsChanged } from '../deps';
import { createDetailUiSchemaResolver } from '../detail-uischema';

@Component({
  selector: 'app-array-layout-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-3" [style.display]="hidden ? 'none' : null">
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
      @for (item of itemProps; track $index) {
        <div class="border border-border rounded-lg p-4 flex flex-col gap-3">
          <jsonforms-outlet [renderProps]="item" />
          @if (isEnabled()) {
            <div class="flex gap-4">
              @if (showSortButtons()) {
                <button type="button" (click)="up($index)" [disabled]="$index === 0"
                  class="text-xs text-accent hover:underline disabled:opacity-50 disabled:cursor-not-allowed">
                  {{ translations.up }}
                </button>
                <button type="button" (click)="down($index)" [disabled]="$index === itemProps.length - 1"
                  class="text-xs text-accent hover:underline disabled:opacity-50 disabled:cursor-not-allowed">
                  {{ translations.down }}
                </button>
              }
              <button type="button" (click)="remove($index)"
                class="text-xs text-error hover:underline">
                {{ translations.removeTooltip }}
              </button>
            </div>
          }
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule],
})
export class ArrayLayoutRenderer
  extends JsonFormsAbstractControl<StatePropsOfArrayLayout>
  implements OnInit
{
  noData = false;
  translations: ArrayTranslations = {};
  addItem!: (path: string, value: unknown) => () => void;
  moveItemUp!: (path: string, index: number) => () => void;
  moveItemDown!: (path: string, index: number) => () => void;
  removeItems!: (path: string, toDelete: number[]) => () => void;
  detailUiSchema?: UISchemaElement;
  /** Also determines how many items are rendered. */
  itemProps: OwnPropsOfRenderer[] = [];
  private resolveDetailUiSchema = createDetailUiSchemaResolver();
  private itemPropsDeps: unknown[] | undefined;
  private changeDetectorRef = inject(ChangeDetectorRef);

  protected override mapToProps(
    state: JsonFormsState,
  ): StatePropsOfArrayLayout & { translations: ArrayTranslations } {
    const props = mapStateToArrayLayoutProps(state, this.getOwnProps());
    const t =
      state.jsonforms.i18n?.translate ?? defaultJsonFormsI18nState.translate;
    const translations = getArrayTranslations(
      t,
      arrayDefaultTranslations,
      props.i18nKeyPrefix ?? '',
      props.label,
    );
    return { ...props, translations };
  }

  showSortButtons(): boolean {
    return !!this.uischema?.options?.['showSortButtons'];
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

  override mapAdditionalProps(
    props: ArrayLayoutProps & { translations: ArrayTranslations },
  ) {
    this.noData = !props.data || props.data === 0;
    this.translations = props.translations;
    this.detailUiSchema = this.resolveDetailUiSchema(
      props,
      this.isEnabled(),
      props.uischema.scope,
    );
    this.updateItemProps(props, this.detailUiSchema);
    // OnPush: changes without an event in this view must schedule the check
    this.changeDetectorRef.markForCheck();
  }

  /**
   * Not created in the template: each new object makes the outlet deep clone
   * the whole form state.
   */
  private updateItemProps(
    props: ArrayLayoutProps,
    detailUiSchema: UISchemaElement,
  ): void {
    // `props.data` is the item count, but it is only derived from `length`, so
    // it is `undefined` for data that isn't an array. Treat that as no items.
    const itemCount = props.data > 0 ? props.data : 0;
    const deps = [detailUiSchema, props.schema, props.path, itemCount];
    if (!depsChanged(this.itemPropsDeps, deps)) {
      return;
    }
    this.itemPropsDeps = deps;

    this.itemProps = Array.from({ length: itemCount }, (_unused, index) => ({
      schema: props.schema,
      path: Paths.compose(props.path, `${index}`),
      uischema: detailUiSchema,
    }));
  }
}

export const arrayLayoutTester: RankedTester = rankWith(
  4,
  isObjectArrayWithNesting,
);
