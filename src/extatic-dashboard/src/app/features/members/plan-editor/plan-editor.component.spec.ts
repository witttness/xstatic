import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { MembershipPlanEditorComponent, slugify } from './plan-editor.component';

describe('MembershipPlanEditorComponent', () => {
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MembershipPlanEditorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  function open(planId?: string) {
    const fixture = TestBed.createComponent(MembershipPlanEditorComponent);
    fixture.componentRef.setInput('appSlug', 'app');
    if (planId) fixture.componentRef.setInput('planId', planId);
    fixture.detectChanges();
    return fixture;
  }

  it('slugifies names', () => {
    expect(slugify('Gold Supporter 2026!')).toBe('gold-supporter-2026');
  });

  it('validates locally before calling the API', () => {
    const editor = open().componentInstance;
    editor.form.name = '';
    editor.form.slug = 'Bad Slug';
    editor.form.duration_days = 0;
    editor.form.max_members = -1;
    editor.save();
    expect(Object.keys(editor.errors())).toEqual(['name', 'slug', 'duration_days', 'max_members']);
    http.expectNone('/api/apps/app/members/plans');
  });

  it('creates a plan with a derived slug, trimmed benefits and null limits', () => {
    const editor = open().componentInstance;
    editor.form.name = 'Gold Members';
    editor.onNameChange();
    editor.form.benefits = ' Newsletter \n\n Discounts ';
    editor.form.duration_days = null;
    editor.form.price_label = '  ';
    editor.save();

    const req = http.expectOne('/api/apps/app/members/plans');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({
      name: 'Gold Members', slug: 'gold-members', benefits: ['Newsletter', 'Discounts'],
      duration_days: null, max_members: null, price_label: null, self_signup_enabled: true,
    });
    req.flush({ id: 'p1' });
    expect(router.navigate).toHaveBeenCalledWith(['/apps', 'app', 'members', 'plans']);
  });

  it('loads an existing plan, keeps its slug and saves with PUT', () => {
    const fixture = open('p1');
    http.expectOne('/api/apps/app/members/plans/p1').flush({
      id: 'p1', name: 'Standard', slug: 'standard', description: null, price_label: '$40 / year', duration_days: 365,
      requires_approval: true, self_signup_enabled: true, max_members: 50, benefits: ['A', 'B'], is_active: true,
      sort_order: 2, active_members: 3, total_memberships: 4, created_at: '', updated_at: '',
    });
    const editor = fixture.componentInstance;
    expect(editor.form.benefits).toBe('A\nB');
    editor.form.name = 'Standard Plus';
    editor.onNameChange();
    expect(editor.form.slug).toBe('standard');
    editor.save();

    const req = http.expectOne('/api/apps/app/members/plans/p1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toMatchObject({ name: 'Standard Plus', slug: 'standard', max_members: 50, requires_approval: true });
    req.flush({});
  });

  it('maps API field errors onto the form', () => {
    const editor = open().componentInstance;
    editor.form.name = 'Dup';
    editor.form.slug = 'dup';
    editor.save();
    http.expectOne('/api/apps/app/members/plans').flush(
      { detail: "A plan with slug 'dup' already exists" }, { status: 409, statusText: 'Conflict' });
    expect(editor.error()).toContain('already exists');
    expect(editor.saving()).toBe(false);
  });
});
