import {
  ChangeDetectorRef,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import {
  JsonFormsAngularService,
  JsonFormsBaseRenderer,
} from '@jsonforms/angular';
import {
  JsonFormsState,
  LabelElement,
  mapStateToLabelProps,
  OwnPropsOfLabel,
  RankedTester,
  rankWith,
  uiTypeIs,
} from '@jsonforms/core';

@Component({
  selector: 'app-label-renderer',
  standalone: true,
  template: `
    <p class="text-sm font-medium" [style.display]="visible ? null : 'none'">
      {{ label }}
    </p>
  `,
})
export class LabelRenderer
  extends JsonFormsBaseRenderer<LabelElement>
  implements OnInit
{
  label?: string;
  visible = true;

  private jsonFormsService = inject(JsonFormsAngularService);
  private changeDetectorRef = inject(ChangeDetectorRef);

  ngOnInit() {
    this.addSubscription(
      this.jsonFormsService.$state.subscribe({
        next: (state: JsonFormsState) => {
          const props = mapStateToLabelProps(
            state,
            this.getOwnProps() as OwnPropsOfLabel,
          );
          this.visible = props.visible;
          this.label = props.text;
          this.changeDetectorRef.markForCheck();
        },
      }),
    );
  }
}

export const labelRendererTester: RankedTester = rankWith(4, uiTypeIs('Label'));
