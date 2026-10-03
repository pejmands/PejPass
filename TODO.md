# TODO

## Recovery Codes

- [ ] Add dedicated recovery-code support for entries, instead of requiring users to store a multiline list in a generic custom field.
  - Store the codes inside the encrypted vault.
  - Keep codes hidden by default and allow copying an individual code.
  - Track which codes have been used and show the number of unused codes remaining.
  - Allow users to mark a code as used; prevent accidental reuse where practical.
  - Ensure editing, history/restore, and deletion behavior are consistent with the existing entry and custom-field model.
  - Include the data in encrypted vault backups and preserve it through import/export where applicable.

## History Retention and Storage

- [ ] Keep entry history snapshots indefinitely unless the user explicitly deletes them; do not automatically delete snapshots based on age.
- [ ] If history storage becomes a practical concern, consider showing the space used by history and offering user-controlled cleanup instead of silently deleting old snapshots.
