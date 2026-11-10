import { ChangeDetectionStrategy, Component } from '@angular/core';
import { JsonFormsModule } from '@jsonforms/angular';
import {
  RankedTester,
  rankWith,
  uiTypeIs,
  VerticalLayout,
} from '@jsonforms/core';
import {
  LayoutRenderer,
  LayoutChildrenRenderPropsPipe,
} from './layout.renderer';

@Component({
  selector: 'app-vertical-layout-renderer',
  standalone: true,
  template: `
    <div class="flex flex-col gap-5" [style.display]="hidden ? 'none' : null">
      @for (
        props of uischema | layoutChildrenRenderProps : schema : path;
        track trackElement($index, props)
      ) {
        <jsonforms-outlet [renderProps]="props" />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule, LayoutChildrenRenderPropsPipe],
})
export class VerticalLayoutRenderer extends LayoutRenderer<VerticalLayout> {}
export const verticalLayoutTester: RankedTester = rankWith(
  1,
  uiTypeIs('VerticalLayout'),
);
