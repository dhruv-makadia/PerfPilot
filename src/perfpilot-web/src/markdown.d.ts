/** Markdown files are bundled as plain text (see "loader" in angular.json). */
declare module '*.md' {
  const content: string;
  export default content;
}
