# Contract: Dashboard Collection Items

## Navigation and permissions

- The Collection list provides an **Items** action separate from **Edit Collection**.
- An Items view belongs to one App and Collection. It shows loading, empty, error, and paged
  states; Item identifier, readable data preview, created time, and updated time are visible.
- The App's `can_manage_items` capability controls the visibility of **New Item** and **Edit**
  actions. Viewer deep links to write screens show a read-only/access-denied state. Every save
  is still checked by the Platform API.
- A member may open an Item detail from the list. An Item missing from the selected Collection
  shows not found without showing data from another Collection or App.

## Entry modes

| Collection schema | Entry modes | Initial mode |
|-------------------|-------------|--------------|
| None | Raw JSON only | Raw JSON |
| Representable object schema | Derived form and raw JSON | Derived form |
| Unsupported or ambiguous structural schema | Raw JSON only, with an explanation | Raw JSON |

The derived form supports defined object properties, required markers, descriptions, default
values, strings, numbers, integers, booleans, enumerations, nested objects, and homogeneous
arrays. Arrays expose add/remove controls only when the schema allows the result. The form
does not claim to validate the full JSON Schema vocabulary; server validation is decisive.
Structural composition or reference patterns that cannot be rendered and round-tripped
safely cause a raw-only fallback. A representable form must retain existing data properties
that it does not display, or refuse the form switch with an explanation if doing so cannot be
guaranteed.

Raw entry accepts a JSON object. Empty or malformed text is flagged before submit. A valid
draft is preserved when changing modes. If a raw draft cannot be represented in the form, the
raw text and draft remain untouched and the user receives an explanation. Editing loads the
current Item data and version. An Item made invalid by a later schema change remains viewable;
its next save requires conformance to the current schema.

## Save feedback

| Outcome | User-visible result | Draft state |
|---------|---------------------|-------------|
| Create success | Confirmation and link/navigation to the created Item or Items view | Saved data visible |
| Update success | Confirmation and refreshed version/data | Saved data visible |
| Input or schema failure | Field/data-path errors where available | Preserved |
| Permission failure | Access denied message | Preserved |
| Stale version | Explain newer data exists; offer reload before retry | Preserved until explicit reload |
| Service or connection failure | No success message; retry guidance | Preserved |

The editor must not silently overwrite a newer Item or replace an invalid draft during mode
switching. Loading and saving states prevent duplicate submit clicks without erasing the draft.

## Accessibility and usability

Controls have visible labels, required-field cues, keyboard access, and errors associated
with the affected fields or raw editor. Loading and save results are announced in a way that
does not require color alone. Long JSON values remain readable in the raw editor; the list
uses a bounded preview so one Item does not overwhelm the page.
