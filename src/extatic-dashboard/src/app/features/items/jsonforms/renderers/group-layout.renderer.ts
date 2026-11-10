import { ChangeDetectionStrategy, Component } from '@angular/core';
import { JsonFormsModule } from '@jsonforms/angular';
import { GroupLayout, RankedTester, rankWith, uiTypeIs } from '@jsonforms/core';
import {
  LayoutRenderer,
  LayoutChildrenRenderPropsPipe,
} from './layout.renderer';

@Component({
  selector: 'app-group-layout-renderer',
  standalone: true,
  template: `
    <fieldset
      class="border border-border rounded-lg p-4 flex flex-col gap-5"
      [style.display]="hidden ? 'none' : null"
    >
      @if (label) {
        <legend class="text-xs font-medium text-text-muted uppercase tracking-wider px-1">
          {{ label }}
        </legend>
      }
      @for (
        props of uischema | layoutChildrenRenderProps : schema : path;
        track trackElement($index, props)
      ) {
        <jsonforms-outlet [renderProps]="props" />
      }
    </fieldset>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [JsonFormsModule, LayoutChildrenRenderPropsPipe],
})
export class GroupLayoutRenderer extends LayoutRenderer<GroupLayout> {}
export const groupLayoutTester: RankedTester = rankWith(1, uiTypeIs('Group'));
