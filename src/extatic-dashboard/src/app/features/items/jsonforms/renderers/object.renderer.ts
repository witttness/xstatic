import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  inject,
} from '@angular/core';
import { JsonFormsModule, JsonFormsControlWithDetail } from '@jsonforms/angular';
import {
  ControlWithDetailProps,
  Generate,
  GroupLayout,
  isObjectControl,
  OwnPropsOfRenderer,
  RankedTester,
  rankWith,
  UISchemaElement,
} from '@jsonforms/core';
import { startCase } from '../start-case';
import { createDetailUiSchemaResolver } from '../detail-uischema';

@Component({
  selector: 'app-object-renderer',
  standalone: true,
  template: `
    <div
      class="border border-border rounded-lg p-4"
      [style.display]="hidden ? 'none' : null"
    >
      @if (renderProps) {
        <jsonforms-outlet [renderProps]="renderProps" />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule],
})
export class ObjectControlRenderer extends JsonFormsControlWithDetail {
  detailUiSchema?: UISchemaElement;
  /** Bound as a whole, as the outlet only re-dispatches when this is set. */
  renderProps?: OwnPropsOfRenderer;
  private resolveDetailUiSchema = createDetailUiSchemaResolver();
  private changeDetectorRef = inject(ChangeDetectorRef);

  override mapAdditionalProps(props: ControlWithDetailProps) {
    const detailUiSchema = this.resolveDetailUiSchema(
      props,
      this.isEnabled(),
      props.uischema.scope,
      () => {
        const newSchema = structuredClone(props.schema);
        // delete unsupported operators
        delete newSchema.oneOf;
        delete newSchema.anyOf;
        delete newSchema.allOf;
        const generated = Generate.uiSchema(
          newSchema,
          'Group',
          undefined,
          props.rootSchema,
        );
        if (props.path === '') {
          generated.type = 'VerticalLayout';
        } else {
          (generated as GroupLayout).label = startCase(props.path);
        }
        return generated;
      },
    );
    if (detailUiSchema === this.detailUiSchema) {
      return;
    }
    this.detailUiSchema = detailUiSchema;
    this.renderProps = {
      uischema: detailUiSchema,
      schema: props.schema,
      path: props.path,
    };
    // OnPush: nothing below reports the new detail, so schedule the check
    this.changeDetectorRef.markForCheck();
  }
}
export const objectControlTester: RankedTester = rankWith(2, isObjectControl);
