import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ViewRef,
  inject,
} from '@angular/core';
import { JsonFormsControl } from '@jsonforms/angular';
import {
  EnumOption,
  isEnumControl,
  isOneOfEnumControl,
  JsonFormsState,
  mapStateToEnumControlProps,
  mapStateToOneOfEnumControlProps,
  OwnPropsOfControl,
  OwnPropsOfEnum,
  RankedTester,
  rankWith,
  StatePropsOfControl,
} from '@jsonforms/core';

const template = `
  <div class="flex flex-col gap-1" [style.display]="hidden ? 'none' : null">
    @if (label) {
      <label [for]="id" class="text-xs font-medium text-text-muted uppercase tracking-wider">
        {{ label }}
      </label>
    }
    <select
      [id]="id"
      (change)="onChange($event)"
      [disabled]="!isEnabled()"
      class="w-full bg-background border border-border px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none disabled:opacity-50 disabled:cursor-not-allowed"
    >
      @if (data === undefined || data === null) {
        <option value="" selected></option>
      }
      @for (option of selectOptions; track option.value) {
        <option [value]="option.value" [selected]="option.value === data">
          {{ option.label }}
        </option>
      }
    </select>
    @if (description) {
      <p class="text-xs text-text-muted">{{ description }}</p>
    }
    @if (error) {
      <p class="text-xs text-error" role="alert">{{ error }}</p>
    }
  </div>
`;

@Component({
  selector: 'app-one-of-enum-control-renderer',
  standalone: true,
  template,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OneOfEnumControlRenderer extends JsonFormsControl {
  selectOptions: EnumOption[] = [];
  private changeDetectorRef = inject(ChangeDetectorRef);

  protected override mapToProps(
    state: JsonFormsState,
  ): StatePropsOfControl & OwnPropsOfEnum {
    return mapStateToOneOfEnumControlProps(state, this.getOwnProps());
  }

  override getEventValue = (event: Event) => {
    const selected = (event.target as HTMLSelectElement).value;
    const option = this.selectOptions.find(
      (o) => String(o.value) === selected,
    );
    return option ? option.value : undefined;
  };

  override mapAdditionalProps(props: StatePropsOfControl & OwnPropsOfEnum) {
    this.selectOptions = props.options ?? [];
    if (!(this.changeDetectorRef as ViewRef).destroyed) {
      this.changeDetectorRef.markForCheck();
    }
  }
}

@Component({
  selector: 'app-enum-control-renderer',
  standalone: true,
  template,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EnumControlRenderer extends OneOfEnumControlRenderer {
  protected override mapToProps(
    state: JsonFormsState,
  ): StatePropsOfControl & OwnPropsOfEnum {
    return mapStateToEnumControlProps(state, this.getOwnProps());
  }
}

export const oneOfEnumControlTester: RankedTester = rankWith(
  5,
  isOneOfEnumControl,
);
export const enumControlTester: RankedTester = rankWith(3, isEnumControl);
