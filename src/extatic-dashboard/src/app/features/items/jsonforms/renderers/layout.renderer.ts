import {
  ChangeDetectorRef,
  Component,
  OnInit,
  Pipe,
  PipeTransform,
  inject,
} from '@angular/core';
import {
  JsonFormsAngularService,
  JsonFormsBaseRenderer,
} from '@jsonforms/angular';
import {
  JsonFormsState,
  JsonSchema,
  Layout,
  mapStateToLayoutProps,
  OwnPropsOfRenderer,
  UISchemaElement,
} from '@jsonforms/core';

@Component({
  template: '',
})
export class LayoutRenderer<T extends Layout>
  extends JsonFormsBaseRenderer<T>
  implements OnInit
{
  hidden = false;
  label?: string;

  protected jsonFormsService = inject(JsonFormsAngularService);
  protected changeDetectionRef = inject(ChangeDetectorRef);

  ngOnInit() {
    this.addSubscription(
      this.jsonFormsService.$state.subscribe({
        next: (state: JsonFormsState) => {
          const props = mapStateToLayoutProps(state, this.getOwnProps());
          this.label = props.label;
          this.hidden = !props.visible;
          this.changeDetectionRef.markForCheck();
        },
      }),
    );
  }

  trackElement(_index: number, renderProp: OwnPropsOfRenderer): string {
    return renderProp
      ? renderProp.path + JSON.stringify(renderProp.uischema)
      : '';
  }
}

@Pipe({ name: 'layoutChildrenRenderProps' })
export class LayoutChildrenRenderPropsPipe implements PipeTransform {
  transform(
    uischema: Layout,
    schema: JsonSchema,
    path: string,
  ): OwnPropsOfRenderer[] {
    const elements = (uischema.elements || []).map((el: UISchemaElement) => ({
      uischema: el,
      schema: schema,
      path: path,
    }));
    return elements;
  }
}
