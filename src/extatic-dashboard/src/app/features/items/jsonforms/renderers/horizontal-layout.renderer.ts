import { ChangeDetectionStrategy, Component } from '@angular/core';
import { JsonFormsModule } from '@jsonforms/angular';
import {
  HorizontalLayout,
  RankedTester,
  rankWith,
  uiTypeIs,
} from '@jsonforms/core';
import {
  LayoutRenderer,
  LayoutChildrenRenderPropsPipe,
} from './layout.renderer';

@Component({
  selector: 'app-horizontal-layout-renderer',
  standalone: true,
  template: `
    <div
      class="flex flex-row flex-wrap gap-5"
      [style.display]="hidden ? 'none' : null"
    >
      @for (
        props of uischema | layoutChildrenRenderProps : schema : path;
        track trackElement($index, props)
      ) {
        <div class="min-w-0 flex-1 basis-64">
          <jsonforms-outlet [renderProps]="props" />
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule, LayoutChildrenRenderPropsPipe],
})
export class HorizontalLayoutRenderer extends LayoutRenderer<HorizontalLayout> {}
export const horizontalLayoutTester: RankedTester = rankWith(
  1,
  uiTypeIs('HorizontalLayout'),
);
